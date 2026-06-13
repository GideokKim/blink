# Blink

[![Release](https://img.shields.io/github/v/release/GideokKim/blink?label=release)](https://github.com/GideokKim/blink/releases/latest)
[![License](https://img.shields.io/badge/license-GPL--3.0-blue)](LICENSE.ko.md)
[![커피 한 잔 사주기](https://img.shields.io/badge/%E2%98%95%20%EC%BB%A4%ED%94%BC%20%ED%95%9C%20%EC%9E%94%20%EC%82%AC%EC%A3%BC%EA%B8%B0-%EC%B9%B4%EC%B9%B4%EC%98%A4%ED%8E%98%EC%9D%B4-FFCD00?logo=kakaotalk&logoColor=black)](https://qr.kakaopay.com/Ej7nHHsn0)

> **파일 이름이 기억나지 않아도 괜찮아요. 내용 한 조각만 기억나면 됩니다.**

Blink는 **왼쪽 Alt + Space** 한 번으로 파일 이름과 **문서 속 내용까지** 즉시 찾아주는
Windows 검색 런처입니다. 한글 검색에 강하고, 문서 수백만 개가 쌓인 NAS에서도 가볍게
돌아갑니다.

> 🌐 Languages: [English](README.en.md) · **한국어**

<!-- TODO: 스포트라이트 검색 GIF/스크린샷 -->

## 📥 설치 — 30초면 충분해요

1. **[최신 버전 다운로드](https://github.com/GideokKim/blink/releases/latest)** — `Blink-Setup-x.y.z.exe` 하나만 받으면 됩니다
2. 실행해서 설치 — **관리자 권한 불필요**, .NET 같은 별도 프로그램 설치도 필요 없습니다
3. 트레이의 Blink 아이콘 → **설정**에서 검색할 폴더를 추가
4. **왼쪽 Alt + Space** — 끝!

## 🤔 이런 적 있지 않나요?

- "그 파일… 제목은 기억 안 나는데, 안에 '3분기 매출'이라고 적었던 건 확실한데."
- Windows 검색창에 한글 두 글자를 넣었더니 아무것도 안 나온다.
- 회사 NAS에 문서가 수십만 개 — 탐색기 검색은 한참 돌다가 빈손으로 끝난다.
- 다른 검색 도구를 깔아봤지만 한글(hwpx) 문서는 검색이 안 된다.

**Blink는 정확히 이 답답함을 해결하려고 만든 앱입니다.**

## 🆚 무엇이 다른가요

|  | Windows 기본 검색 | 파일명 검색 도구 | **Blink** |
|---|:---:|:---:|:---:|
| 문서 **본문** 검색 | 제한적 | ✗ | ✅ |
| 한글 **두 글자** 부분 검색 | ✗ | △ | ✅ |
| PDF · Office · **한컴(hwpx)** 본문 | ✗ | ✗ | ✅ |
| 수백만 파일 NAS | 느림 | △ | ✅ |
| 보안 통제(EDR) 환경 대응 | — | — | ✅ |

## ✨ 주요 기능

- **⚡ 즉시 검색** — 트레이에 조용히 상주하다가 단축키 한 번에 스포트라이트 창이
  떠오릅니다. 결과에는 검색어가 매치된 문장 미리보기가 함께 표시돼요.
- **📄 본문까지 검색** — `.pdf`, `.docx`, `.xlsx`, `.pptx`, `.hwpx`(한컴), `.txt`,
  `.md`, `.rtf` 문서의 내용을 인덱싱합니다. 그 외 파일은 파일명으로 검색됩니다.
- **🇰🇷 한글에 진심** — "글검"만 입력해도 "한글검색기획안.docx"를 찾아냅니다.
  두 글자 부분 일치, 한/영 혼합 검색 모두 지원합니다.
- **🔄 알아서 최신 상태** — 새로 생기거나 바뀐 파일만 다시 읽는 증분 인덱싱.
  삭제된 파일은 자동으로 정리됩니다.
- **🧹 잡음 없는 결과** — 임시 파일, Office 잠금 파일(`~$…`), OS 메타데이터는 자동
  제외. 폴더에 `.blinkignore` 파일(gitignore 문법)을 두면 제외 규칙을 직접 만들 수
  있습니다.
- **🔒 데이터는 내 PC에** — Blink에는 서버가 없습니다. 인덱싱과 검색 모두 내
  컴퓨터에서 처리되고, 인덱스는 `%APPDATA%\Blink`의 로컬 데이터베이스에 저장됩니다.

## 🏢 회사 보안망에서도 돌아가도록 설계했습니다

대규모·통제 환경은 Blink가 가장 공들인 부분입니다.

- 문서가 수백만 개인 트리도 **메모리 사용량이 일정**하게 유지되는 인덱싱 설계 —
  이미지가 수백만 장인 폴더는 가상 항목 하나로 묶어 인덱스를 가볍게 유지합니다.
- NAS/SMB 읽기를 **별도 프로세스(인덱서 워커)로 격리** — EDR/백신이 메인 앱의
  네트워크 읽기를 차단하는 환경을 위한 구조입니다. 관리자는 워커 프로세스 하나만
  허용 목록에 등록하면 됩니다.
- 드라이브 루트(`L:\`) 전체를 자식 폴더 단위로 나눠 인덱싱하는 `drive_split` 지원.

## ❓ 자주 묻는 질문

**Q. 시스템 요구사항은 어떻게 되나요?**
Windows 10 이상(64비트)이면 됩니다. 필요한 구성요소가 설치 파일에 모두 포함되어
있어 별도 런타임 설치가 필요 없습니다.

**Q. 내 문서가 외부로 전송되지는 않나요?**
Blink에는 서버가 없습니다. 인덱싱·검색 전 과정이 내 PC 안에서 처리되며, 설정과
인덱스는 `%APPDATA%\Blink`(`config.json`, `index.db`)에 저장됩니다.

**Q. Windows를 켤 때 자동으로 실행되게 하려면?**
설치 마법사의 "Windows 시작 시 Blink 자동 실행" 옵션을 켜거나, 설치 후 설정에서
자동 시작 토글을 켜면 됩니다.

**Q. 특정 폴더나 파일을 검색에서 빼고 싶어요.**
제외하고 싶은 폴더에 `.blinkignore` 파일을 만들고 gitignore 문법으로 규칙을 적으면
됩니다. 임시 파일류는 규칙 없이도 자동 제외됩니다.

<details>
<summary><b>🛠 개발자를 위한 정보</b></summary>

### 기술 개요

.NET 8 기반. 검색 엔진은 SQLite FTS5 위에 한글 n-gram 토크나이저(2/3-gram, NFC
정규화)를 올린 구조이고, UI는 WPF 스포트라이트 창입니다. 검색 **엔진**(`Blink.Core`)과
**인덱서 워커**는 크로스플랫폼에서 단위 테스트되며(103개), **WPF 앱**(`Blink.App`)은
`net8.0-windows` 대상이라 Windows에서 빌드합니다 — [`BUILD-WINDOWS.md`](BUILD-WINDOWS.md) 참고.

### 저장소 구성

| 프로젝트 | 대상 | 설명 |
|---|---|---|
| `Blink.Core` | `net8.0` | 검색 엔진: 토크나이저, FTS5 저장소, 파서, 인덱서, pruner, 번들링, 워커 프로토콜 |
| `Blink.Indexer.Worker` | `net8.0` | 독립 인덱싱 프로세스 (EDR 격리) |
| `Blink.Cli` | `net8.0` | 헤드리스 도구: `index` / `search` / `status` / `prune` |
| `Blink.Core.Tests` | `net8.0` | xUnit 테스트 모음 |
| `Blink.App` | `net8.0-windows` | WPF 스포트라이트 UI (**Windows 전용**, `Blink.sln` 미포함) |

### 엔진 빠른 시작 (.NET 8이 있는 모든 OS)

```bash
dotnet test Blink.Core.Tests -c Release                  # 테스트
dotnet run --project Blink.Cli -- index "/path/to/docs"  # 인덱싱
dotnet run --project Blink.Cli -- search 한글검색          # 검색
dotnet run --project Blink.Cli -- status                 # DB 경로, 문서 수, 폴더
```

### 인스톨러 & 릴리즈

사용자 단위 Inno Setup 인스톨러는 [`installer/`](installer/README.md), 태그 기반
자동 릴리즈는 [`RELEASING.md`](RELEASING.md) 참고.

</details>

## 라이선스

**GPL-3.0 오픈소스** — Blink는 [GNU General Public License v3.0](LICENSE) 하에
배포됩니다. 누구나 무료로 설치·사용하고, 소스를 열람·수정·재배포할 수 있습니다.
다만 수정본이나 파생물을 배포할 때는 **같은 GPL-3.0으로 소스와 함께 공개**해야
합니다(카피레프트). 자세한 내용: [LICENSE](LICENSE) ([한국어 안내](LICENSE.ko.md))

Blink가 시간을 아껴줬다면 커피 한 잔으로 응원해주세요. ☕

[☕ 카카오페이로 커피 한 잔 사주기](https://qr.kakaopay.com/Ej7nHHsn0)
