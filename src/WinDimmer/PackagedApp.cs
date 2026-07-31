using Windows.ApplicationModel;
using WinDimmer.Native;

namespace WinDimmer;

/// <summary>
/// MSIX 패키지로 실행 중인지 감지하고, 패키지 환경의 자동 실행(StartupTask)을 다룬다.
/// MSIX 컨테이너에서는 HKCU Run 키 쓰기가 패키지 전용 하이브로 가상화되어 실제 자동 실행으로
/// 이어지지 않으므로, 매니페스트에 선언한 StartupTask API를 대신 써야 한다.
/// </summary>
internal static class PackagedApp
{
    /// <summary>packaging/Package.appxmanifest의 desktop:StartupTask TaskId와 일치해야 한다.</summary>
    private const string StartupTaskId = "WinDimmerStartup";

    private const int AppModelErrorNoPackage = 15700;

    private static readonly Lazy<bool> _isPackaged = new(static () =>
    {
        // 패키지 컨텍스트가 없으면 APPMODEL_ERROR_NO_PACKAGE, 있으면 버퍼가 짧다는 오류가 온다 —
        // 이름 자체는 필요 없고 존재 여부만 가르면 된다.
        uint length = 0;
        return Kernel32.GetCurrentPackageFullName(ref length, []) != AppModelErrorNoPackage;
    });

    internal static bool IsPackaged => _isPackaged.Value;

    /// <summary>
    /// 실제 StartupTask 활성 상태. 사용자가 작업 관리자 > 시작 앱에서 직접 끌 수 있으므로
    /// config에 저장된 값과 어긋날 수 있다 — 화면에 보여줄 때는 이 값을 기준으로 삼는다.
    /// </summary>
    internal static bool IsAutoStartEnabled()
    {
        StartupTask task = StartupTask.GetAsync(StartupTaskId).GetAwaiter().GetResult();
        return task.State is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
    }

    /// <summary>
    /// 자동 실행을 켜거나 끈 뒤의 실제 StartupTask 상태를 돌려준다. 사용자가 작업 관리자에서
    /// 직접 껐거나(DisabledByUser) 시스템 정책이 막은 경우(DisabledByPolicy)에는 앱이 되살릴
    /// 수 없어, 켜 달라고 요청해도 꺼진 상태가 돌아온다 — 호출자가 상태별로 안내한다.
    /// </summary>
    internal static StartupTaskState SetAutoStart(bool enable)
    {
        StartupTask task = StartupTask.GetAsync(StartupTaskId).GetAwaiter().GetResult();

        if (!enable)
        {
            task.Disable();
            return StartupTaskState.Disabled;
        }

        return task.RequestEnableAsync().GetAwaiter().GetResult();
    }
}
