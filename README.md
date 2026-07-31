# WinDimmer

원하는 창만 골라 어둡게 만드는 Windows 트레이 유틸리티. 여러 창을 동시에, 창마다 다른 밝기로.

## 설치

[Releases](../../releases)에서 최신 zip을 받아 **전체를 압축 해제**한 뒤 `WinDimmer.exe`를 실행한다.
처음 실행할 때 SmartScreen 경고가 뜨면 "추가 정보 → 실행"을 누른다 — 서명 인증서 없이 배포되는 개인 개발 프로그램의 공통 절차다.

- 기본 zip은 .NET 설치가 필요 없다. `lite` zip은 용량이 작은 대신 [.NET 8 데스크톱 런타임](https://dotnet.microsoft.com/download/dotnet/8.0)이 필요하다
- Windows 10 (1809+) 또는 Windows 11, x64

## 개발

```bash
dotnet test
```

릴리스는 `v*` 태그를 push하면 GitHub Actions가 빌드해 올리고, 각 zip에 출처 증명(provenance attestation)이 첨부된다. 상세 문서는 개편 중.

## 라이선스

[MIT](LICENSE)
