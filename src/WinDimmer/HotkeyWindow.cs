using System.Diagnostics;
using WinDimmer.Native;
using static WinDimmer.Native.Constants;

namespace WinDimmer;

/// <summary>
/// 메시지 전용 창에서 전역 핫키를 받는다. 동작(<see cref="HotkeyAction"/>)마다 고정 ID를 부여하고
/// 사전 기반으로 등록·해제한다. 재등록은 원자적이다 — 실패하면 직전에 동작하던 설정으로 되돌린다.
/// </summary>
internal sealed class HotkeyWindow : NativeWindow, IDisposable
{
    /// <summary>가장 최근에 성공적으로 등록된(=현재 유효한) 설정. Suspend 중에도 보관된다.</summary>
    private Dictionary<HotkeyAction, HotkeySpec> _current = new();

    /// <summary><see cref="_current"/>가 실제로 OS에 등록돼 있는지. Suspend 상태면 false.</summary>
    private bool _registered;

    private bool _disposed;

    public event Action<HotkeyAction>? Pressed;

    /// <summary>지금 실제로 OS에 등록돼 있는 단축키의 스냅샷. Suspend 중에는 마지막으로 등록됐던 값을 반환한다.</summary>
    public IReadOnlyDictionary<HotkeyAction, HotkeySpec> Current => new Dictionary<HotkeyAction, HotkeySpec>(_current);

    public HotkeyWindow()
    {
        CreateHandle(new CreateParams { Parent = HWND_MESSAGE });
    }

    private static int IdFor(HotkeyAction action) => (int)action + 1;

    /// <summary>
    /// 새 단축키 집합을 등록한다.
    /// 1) 지금 등록된 것을 전부 해제하고 2) 새 조합을 하나씩 등록한다.
    /// 하나라도 실패하면 성공한 것까지 전부 해제하고 직전 설정으로 복원한 뒤,
    /// 실패한 동작 목록을 반환한다. 복원마저 실패하면 그 동작도 목록에 넣고 로그를 남긴다.
    /// 전부 성공하면 빈 목록을 반환한다.
    /// </summary>
    public IReadOnlyList<HotkeyAction> TryApply(IReadOnlyDictionary<HotkeyAction, HotkeySpec> desired)
    {
        // Suspend 중에 호출해도 여기서 새로 등록하며 suspend 상태를 그대로 끝낸다 — 의도된 동작이다.
        UnregisterSet(_current);
        _registered = false;

        var succeeded = new Dictionary<HotkeyAction, HotkeySpec>();
        var failed = new List<HotkeyAction>();

        foreach ((HotkeyAction action, HotkeySpec spec) in desired)
        {
            if (TryRegister(action, spec))
                succeeded[action] = spec;
            else
                failed.Add(action);
        }

        if (failed.Count > 0)
        {
            // 부분적으로 성공한 새 조합을 전부 해제하고 직전(마지막으로 동작했던) 설정을 복원한다.
            UnregisterSet(succeeded);

            var restored = new Dictionary<HotkeyAction, HotkeySpec>();
            foreach ((HotkeyAction action, HotkeySpec spec) in _current)
            {
                if (TryRegister(action, spec))
                {
                    restored[action] = spec;
                }
                else
                {
                    // 롤백마저 실패했다 — 조용히 넘어가지 않는다.
                    if (!failed.Contains(action)) failed.Add(action);
                    Trace.WriteLine($"핫키 롤백 실패: {action} ({spec.Display()})");
                }
            }

            _current = restored;
            _registered = restored.Count > 0;
            return failed;
        }

        _current = succeeded;
        _registered = true;
        return Array.Empty<HotkeyAction>();
    }

    private bool TryRegister(HotkeyAction action, HotkeySpec spec)
    {
        // 수정키 없는 조합은 모든 앱의 그 키를 가로채므로 애초에 등록 대상이 아니다.
        if (!spec.IsValid) return false;

        (uint mods, uint vk) = spec.ToWin32();
        return User32.RegisterHotKey(Handle, IdFor(action), mods | MOD_NOREPEAT, vk);
    }

    private void UnregisterSet(IReadOnlyDictionary<HotkeyAction, HotkeySpec> set)
    {
        foreach (HotkeyAction action in set.Keys)
            User32.UnregisterHotKey(Handle, IdFor(action));
    }

    /// <summary>
    /// 등록된 핫키를 전부 해제한다. 보관 중인 설정은 유지해 <see cref="Resume"/>으로 되돌릴 수 있다.
    /// 이미 suspend 상태에서 다시 호출해도 안전하다.
    /// </summary>
    public void Suspend()
    {
        if (!_registered) return;

        UnregisterSet(_current);
        _registered = false;
    }

    /// <summary>
    /// 보관 중인 설정으로 다시 등록한다. Suspend 없이 호출하거나 이미 등록된 상태에서
    /// 다시 호출해도 안전하다(아무 일도 하지 않는다). 재등록하지 못한 동작 목록을 반환한다
    /// (예: 캡처 대화상자가 떠 있는 동안 다른 프로그램이 그 조합을 선점한 경우).
    /// </summary>
    public IReadOnlyList<HotkeyAction> Resume()
    {
        if (_registered || _current.Count == 0) return Array.Empty<HotkeyAction>();

        var restored = new Dictionary<HotkeyAction, HotkeySpec>();
        var failed = new List<HotkeyAction>();
        foreach ((HotkeyAction action, HotkeySpec spec) in _current)
        {
            if (TryRegister(action, spec))
            {
                restored[action] = spec;
            }
            else
            {
                failed.Add(action);
                Trace.WriteLine($"핫키 재개 실패: {action} ({spec.Display()})");
            }
        }

        _current = restored;
        _registered = restored.Count > 0;
        return failed;
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY)
        {
            int id = (int)m.WParam;
            foreach (HotkeyAction action in HotkeyActions.All)
            {
                if (IdFor(action) == id)
                {
                    Pressed?.Invoke(action);
                    break;
                }
            }
        }

        base.WndProc(ref m);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        UnregisterSet(_current);
        DestroyHandle();
    }
}
