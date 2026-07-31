using System.Diagnostics;
using System.Threading;

namespace WinDimmer;

internal static class Program
{
    private const string MutexName = "WinDimmer.SingleInstance.9F2C1A7B";

    [STAThread]
    private static void Main()
    {
        using var mutex = new Mutex(initiallyOwned: true, MutexName, out bool isFirst);
        if (!isFirst)
        {
            MessageBox.Show("WinDimmer가 이미 실행 중입니다.", "WinDimmer",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();

        // 메인 윈도우가 없는 트레이 앱은 처리되지 않은 예외로 조용히 죽으면
        // 사용자가 닫은 것과 구분할 수 없다. 최소한 진단 출력이라도 남긴다.
        // ThreadException을 구독하면 WinForms 기본 ThreadExceptionDialog가 대체되므로,
        // 그 대신 여기서 직접 사용자에게 알린다 — 그러지 않으면 예외가 흔적 없이 사라진다.
        Application.ThreadException += (_, e) =>
        {
            Trace.WriteLine($"처리되지 않은 UI 스레드 예외 — {e.Exception}");
            MessageBox.Show(
                $"예상치 못한 오류가 발생했습니다: {e.Exception.GetType().Name} — {e.Exception.Message}",
                "WinDimmer", MessageBoxButtons.OK, MessageBoxIcon.Error);
        };
        // 프로세스가 이미 종료 중인 경로에서 발생하므로 대화상자는 신뢰할 수 없다. 기록만 남긴다.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Trace.WriteLine($"처리되지 않은 예외 — {e.ExceptionObject}");

        using var manager = new DimManager();
        using var tray = new TrayApp(manager);

        Application.Run();
    }
}
