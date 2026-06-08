# Blink

한글 친화 데스크톱 즉시 검색 런처입니다. **왼쪽 Alt + Space**를 누르고 입력하면 대규모
문서/NAS 트리에서 파일 이름과 *본문*을 함께 검색합니다. .NET 8 기반이며 SQLite FTS5 검색
엔진과 WPF 스포트라이트 UI로 구성됩니다.

> 상태: 검색 **엔진**(`Blink.Core`)과 **인덱서 워커**는 크로스플랫폼에서 완전히 구현·단위
> 테스트되었습니다(103개 테스트). **WPF 앱**(`Blink.App`)은 `net8.0-windows` 대상이라
> Windows에서 빌드·검증해야 합니다 — [`BUILD-WINDOWS.md`](BUILD-WINDOWS.md) 참고.

> 🌐 Languages: [English](README.md) · **한국어**

## 주요 기능

- **한글 인식 전문 검색** — n-gram 토크나이저(한글 2/3-gram)를 SQLite FTS5 위에서 사용,
  NFC 정규화로 2글자 부분 검색과 한/영 혼합 질의가 모두 매칭됩니다.
- **풍부한 본문 추출** — `.txt`/`.md`, `.xlsx`, `.pdf`, `.docx`, `.pptx`, `.hwpx`(한컴),
  `.rtf`의 본문을 인덱싱합니다. 읽을 수 없거나 알 수 없는 파일은 파일명만 인덱싱으로 폴백합니다.
- **증분 인덱싱** — 변경된(또는 새) 파일만 mtime 기준으로 재파싱하고, 삭제된 파일은 가드가
  걸린 pruner가 정리합니다.
- **쓰레기 제외** — Office 잠금 파일(`~$*.xlsx`), 임시 파일, OS 메타데이터 등과, 선택적
  `.blinkignore`(gitignore 문법)를 지원합니다.
- **대규모 대응 번들링** — 정렬된 이름의 이미지가 수백만 개 있는 폴더는 수백만 행 대신 가상
  1엔트리로 축약됩니다. 본문 파일은 개별 검색 그대로 유지됩니다.
- **대규모·통제망 환경 대응**
  - 디스크 백업 스캔 캐시 기반 3-pass 인덱싱으로 수백만 파일에서도 메모리가 일정합니다.
  - 별도 프로세스 **인덱서 워커**가 모든 SMB/NAS 읽기를 메인 앱에서 격리합니다 — 메인
    실행파일의 네트워크 읽기를 차단하는 EDR/AV 환경을 위한 설계입니다.
  - `drive_split`은 드라이브 루트(`L:\`)를 독립적인 자식 폴더 단위로 인덱싱합니다.
- **상주형 UI** — 트레이 아이콘, 전역 단축키, Acrylic 스포트라이트 창, 매치 라인 미리보기,
  자동 시작 토글.

## 저장소 구성

| 프로젝트 | 대상 | 설명 |
|---|---|---|
| `Blink.Core` | `net8.0` | 검색 엔진: 토크나이저, FTS5 저장소, 파서, 인덱서, pruner, 번들링, 워커 프로토콜. 크로스플랫폼, 단위 테스트됨. |
| `Blink.Indexer.Worker` | `net8.0` | 독립 인덱싱 프로세스. 저장 연산을 JSON 라인으로 스트리밍(EDR 격리). |
| `Blink.Cli` | `net8.0` | 헤드리스 도구: `index` / `search` / `status` / `prune`. |
| `Blink.Core.Tests` | `net8.0` | xUnit 테스트 모음(103개). |
| `Blink.App` | `net8.0-windows` | WPF 스포트라이트 UI. **Windows 전용**, `Blink.sln`에 미포함. |

## 빠른 시작 (엔진 — .NET 8이 있는 모든 OS)

```bash
# 테스트 실행
dotnet test Blink.Core.Tests -c Release

# 폴더 인덱싱 후 검색 (헤드리스)
dotnet run --project Blink.Cli -- index "/path/to/docs"
dotnet run --project Blink.Cli -- search 한글검색
dotnet run --project Blink.Cli -- search 글검          # 2-gram 한글 부분 검색
dotnet run --project Blink.Cli -- status               # DB 경로, 문서 수, 폴더
dotnet run --project Blink.Cli -- prune "/path/to/docs"  # 미리보기; --apply로 실제 정리
```

설정과 인덱스 DB는 `%APPDATA%\Blink\`(`config.json`, `index.db`) 또는 플랫폼 동등 경로에
저장됩니다.

## Windows 앱 & 인스톨러

- **WPF 앱 빌드/실행:** [`BUILD-WINDOWS.md`](BUILD-WINDOWS.md)
- **인스톨러 생성:** 가능합니다 — 사용자 단위 Inno Setup 인스톨러가 [`installer/`](installer/README.md)에
  있습니다. Windows에서 앱 + 워커를 publish한 뒤 `iscc installer\blink.iss`를 실행하면
  `Blink-Setup-<버전>.exe`가 생성됩니다(한국어/영어 마법사, 자동 시작 옵션, 인덱서 워커 동봉).
- **릴리즈 배포:** `vX.Y.Z` 태그를 푸시하면 GitHub Actions가 인스톨러를 빌드하고 릴리즈
  노트와 함께 Release를 게시합니다 — [`RELEASING.md`](RELEASING.md) 참고.

## 라이선스

저장소 참고.
