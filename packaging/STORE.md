# Microsoft Store 배포 가이드

왜 Store인가: 개인 개발자 등록이 **무료**이고(2025-09부터), MSIX를 업로드하면 **Microsoft가
직접 재서명**하므로 받는 사람에게 SmartScreen 경고가 전혀 뜨지 않는다. 인증서 구매·갱신이
필요 없고, 업데이트도 Store가 자동 배포한다. GitHub Releases zip 직배포는 서명을 사도
평판이 쌓이기 전까지 경고가 뜨므로, 지인 배포는 Store 링크가 정답이다.

## 1회 준비 (계정·이름 예약)

1. https://partner.microsoft.com/dashboard 에서 **개인(Individual)** 계정으로 등록한다.
   개인 Microsoft 계정 + 신분증 스캔 + 셀피 인증. 무료.
2. Apps and Games → **New product → MSIX/PWA** 로 앱 이름을 예약한다 (예: `WinDimmer` —
   이미 있으면 다른 이름).
3. 제품 페이지 → **Product management → Product identity** 에서 세 값을 확인한다:
   - `Package/Identity/Name` (예: `12345HongGilDong.WinDimmer`)
   - `Package/Identity/Publisher` (예: `CN=A1B2C3D4-E5F6-...`)
   - `Package/Properties/PublisherDisplayName`

## 패키지 빌드

```powershell
.\packaging\build-msix.ps1 -IdentityName "<Identity/Name 값>" `
                           -Publisher "<Identity/Publisher 값>" `
                           -PublisherDisplayName "<PublisherDisplayName 값>"
```

`packaging\out\WinDimmer_<버전>_x64.msix` 가 나온다. **서명하지 않는다** — Store가 한다.
버전은 csproj `<Version>` 을 따르며, Store 규칙상 4번째 자리는 항상 0이다.

## 제출

1. 제품 페이지에서 새 Submission 시작 → **Packages** 에 `.msix` 업로드.
2. **Pricing and availability**: 무료로 설정. 지인에게만 알리고 싶으면
   - *Discoverability* 를 **"Make this product available but not discoverable in the Store"**
     (직접 링크로만 설치 가능) 로 두거나,
   - *Audience* 를 Private audience 로 제한한다.
3. 나이 등급 설문, 설명·스크린샷 등 리스팅을 채우고 제출. 심사는 보통 1~3일.
4. 통과되면 `https://apps.microsoft.com/detail/<StoreId>` 링크를 공유하면 끝.

## 로컬 설치 테스트 (Store 올리기 전)

서명 없는 패키지는 그대로 설치할 수 없다. 두 방법 중 하나:

- **개발자 모드** (설정 → 시스템 → 개발자용 → 개발자 모드 켬) 후:
  ```powershell
  Add-AppxPackage -Register .\packaging\out\staging\AppxManifest.xml
  ```
  staging 폴더의 파일을 그대로 실행하는 등록이라 제거도 간단하다
  (`Get-AppxPackage WinDimmer.Dev | Remove-AppxPackage`).
- 또는 Store 심사를 그냥 통과시킨 뒤 Store에서 받아 확인한다 (심사가 곧 검증이다).

## MSIX 실행 시 zip 배포판과 다른 점

- **자동 실행**: 레지스트리 Run 키 대신 매니페스트의 StartupTask를 쓴다 (코드가 자동 분기,
  [PackagedApp.cs](../src/WinDimmer/PackagedApp.cs)). 작업 관리자 → 시작 앱 목록에 "WinDimmer"로 표시된다.
- **설정 파일 위치**: `%APPDATA%\WinDimmer\config.json` 쓰기가
  `%LOCALAPPDATA%\Packages\<패키지ID>\LocalCache\Roaming\WinDimmer\` 로 가상화된다.
  앱 입장에서는 투명하지만, 파일을 직접 편집하려면 저 경로를 열어야 한다.
- **관리자 권한 실행**: 패키지 앱은 우클릭 "관리자 권한으로 실행"이 없다. 관리자 권한 창을
  지연 없이 디밍하려는 고급 사용자는 zip 배포판을 쓰면 된다 (MSIX에서도 100ms 폴링
  폴백으로 동작 자체는 한다).

## 체크리스트 (첫 제출 전 실제 기기에서 확인)

- [ ] 트레이 아이콘·창 선택·디밍·단축키 동작
- [ ] 설정창 자동 실행 체크 → 재부팅(또는 로그아웃/인) 후 자동 시작 확인
- [ ] 작업 관리자 → 시작 앱에서 끈 뒤, 설정창에서 다시 켜면 안내 풍선이 뜨는지
- [ ] 규칙 저장 후 재실행 시 유지되는지 (가상화된 config 경로 동작 확인)
