namespace WinDimmer;

/// <summary>설정 창의 "디밍 중인 창" 목록에 한 줄로 그려지는 값. 조회 시점의 스냅샷이다.</summary>
/// <param name="Handle">이 줄이 가리키는 창. 목록에서 밝기를 바꾸거나 해제할 때의 키.</param>
/// <param name="Title">창 제목. 빈 문자열일 수 있다(제목 없는 창).</param>
/// <param name="Process">프로세스 이름. 같은 프로그램의 창이 여럿일 때 구분에 쓴다.</param>
/// <param name="Alpha">현재 밝기(0–255).</param>
/// <param name="AlphaIsCustom">사용자가 이 창만 따로 조정했는지.</param>
internal readonly record struct DimmedWindow(
    IntPtr Handle, string Title, string Process, byte Alpha, bool AlphaIsCustom);

/// <summary>
/// 설정 창이 디밍 중인 창들을 보고 조작하기 위한 창구. <see cref="TrayApp"/>이 구현한다.
///
/// 설정 창에 <see cref="DimManager"/>를 통째로 넘기지 않는 이유는, 설정 창이 세션의 수명이나
/// Z-order 전략 같은 내부 사정까지 만질 수 있게 되면 두 곳에서 세션을 조작하는 길이 열리기
/// 때문이다. 여기서 노출하는 것은 "목록을 읽는다 / 밝기를 바꾼다 / 해제한다" 셋뿐이다.
/// </summary>
internal interface IDimmedWindowsView
{
    /// <summary>지금 디밍 중인 창들. 호출할 때마다 제목을 새로 읽는다(제목은 바뀔 수 있다).</summary>
    IReadOnlyList<DimmedWindow> Snapshot();

    /// <summary>창 하나의 밝기를 바꾼다. 그 창은 "개별 지정됨"이 되어 기본 밝기를 따라가지 않게 된다.</summary>
    void SetAlpha(IntPtr target, byte alpha);

    /// <summary>창 하나의 디밍을 해제한다.</summary>
    void Release(IntPtr target);

    /// <summary>목록이 달라졌을 때(창이 추가·제거·해제됐을 때) 발생한다.</summary>
    event Action? Changed;
}
