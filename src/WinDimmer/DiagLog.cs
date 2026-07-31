namespace WinDimmer;

/// <summary>
/// 진단용 순환 로그. 버그 추적이 끝나면 제거할 수 있다.
/// 저수준 훅과 WinEvent 콜백에서 호출되므로 절대 예외를 던지지 않고, 할당을 최소화한다.
/// </summary>
internal static class DiagLog
{
    private const int Capacity = 200;

    private static readonly string?[] _entries = new string?[Capacity];
    private static int _next;   // 다음에 쓸 슬롯
    private static int _count;  // 지금까지 채운 개수 (Capacity에서 멈춘다)
    private static readonly object _gate = new();

    /// <summary>타임스탬프와 함께 기록한다. 절대 예외를 던지지 않는다.</summary>
    public static void Write(string message)
    {
        try
        {
            string line = $"{DateTime.Now:HH:mm:ss.fff} {message}";
            lock (_gate)
            {
                _entries[_next] = line;
                _next = (_next + 1) % Capacity;
                if (_count < Capacity) _count++;
            }
        }
        catch
        {
            // 훅/콜백 스택에서 절대 예외를 새어나가게 하지 않는다.
        }
    }

    /// <summary>오래된 것부터 줄바꿈으로 이어붙여 반환한다.</summary>
    public static string Dump()
    {
        lock (_gate)
        {
            if (_count == 0) return string.Empty;

            var lines = new string[_count];
            // 가장 오래된 항목의 인덱스: 아직 다 안 찼으면 0부터, 다 찼으면 _next부터(가장 오래된 것을 덮어쓸 다음 위치)
            int start = _count < Capacity ? 0 : _next;
            for (int i = 0; i < _count; i++)
                lines[i] = _entries[(start + i) % Capacity] ?? string.Empty;

            return string.Join(Environment.NewLine, lines);
        }
    }

    public static void Clear()
    {
        lock (_gate)
        {
            Array.Clear(_entries, 0, _entries.Length);
            _next = 0;
            _count = 0;
        }
    }
}
