# Blink 업데이트 UX 현대화 리서치

> 작성일: 2026-06-19 · 범위: 리서치(코드 변경 없음) · 산출물: 단일 권고안 + 근거 + 마이그레이션 개요
> 관련 스펙: `.omc/specs/deep-interview-modern-update-ux-research.md`

## 0. TL;DR (단일 권고안)

> **업데이트 전달은 [Velopack](https://velopack.io/)으로 전환하고, "업데이트 있음" 알림은 "전송형 토스트 하나"에 의존하지 말고 *지속 표면(persistent surface)* 중심으로 재설계한다.**
>
> 즉, 알림 안 뜨는 문제를 "더 좋은 토스트"로 푸는 게 아니라, **토스트는 best-effort로 두고, 트레이 아이콘 배지 + 런처 내 인앱 배너라는 항상-보이는 표면으로 발견성을 보장**하는 구조로 바꾼다. 다운로드·적용은 Velopack의 델타 업데이트 + 백그라운드 스테이징 + 1-클릭 재시작으로 현대화한다.

이유 한 줄: **알림이 안 뜨는 근본 원인은 "Win32 balloon이 약해서"가 아니라 "휘발성 알림 하나에 발견성을 100% 의존"하기 때문**이다. OS(Focus Assist/방해 금지/알림 끄기)는 어떤 토스트든 합법적으로 억제할 수 있으므로, 신뢰성은 알림 채널 교체가 아니라 *표면의 다중화*에서 나온다.

---

## 1. 현재 구조 (As-Is)

| 단계 | 구현 | 파일 |
|------|------|------|
| 체크 | GitHub Releases 폴링(`latest.json` CDN 우선 + REST 폴백), 실패 시 `null` | `Blink.Core/Update/UpdateChecker.cs` |
| 스케줄 | `DispatcherTimer` 시작 30초 후 + 24h 간격 | `Blink.App/Update/UpdateService.cs:19` |
| **알림** | **`Forms.NotifyIcon.ShowBalloonTip(10000, …)`** | `Blink.App/App.xaml.cs:217` |
| 다운로드 | `%TEMP%\Blink`에 `Blink-Setup-*.exe` 다운로드(진행률 보고) | `UpdateService.DownloadInstallerAsync` |
| 적용 | Inno Setup `/SILENT /SUPPRESSMSGBOXES /NORESTART` 실행 후 앱 종료 | `UpdateService.LaunchInstaller` |
| 패키징/배포 | Inno Setup(`installer/blink.iss`), self-contained 단일 exe, 태그 기반 GitHub Actions | `.github/workflows/release.yml` |

### 1.1 알림이 안 뜨는 근본 원인 진단

`NotifyIcon.ShowBalloonTip`은 Win32 셸 알림(NIM_MODIFY + NIF_INFO) API다. Windows 10/11에서는 이게 내부적으로 Action Center 토스트로 라우팅되지만 다음 이유로 **누락이 잦다**:

1. **Focus Assist / 방해 금지(Do Not Disturb) / 조용한 시간**: 사용자가 켜두면 모든 토스트가 합법적으로 억제된다. balloon은 Action Center에 **누적조차 안 되는 경우**가 있어 "사라지면 끝".
2. **알림 설정 OFF / 우선순위 낮음**: 앱별 알림이 꺼져 있거나 우선순위가 낮으면 표시되지 않음.
3. **레거시 API의 한계**: balloon tip은 만료(10초) 후 흔적이 없다. 사용자가 그 순간 화면을 안 보면 영구 소실.
4. **AppUserModelID 부재**: 적절한 AUMID 없이 뜬 알림은 Action Center에서 앱 그룹화/재노출이 안 되거나 무시될 수 있음.

→ 핵심 통찰: **어떤 알림 채널로 바꿔도 OS 억제는 못 막는다.** 따라서 "더 나은 토스트"는 부분 개선일 뿐, 진짜 해법은 *항상 보이는 비휘발성 표면*을 추가하는 것.

---

## 2. 옵션 비교

평가 제약: ① 배포 스택 전면 교체 허용 ② **무료 GitHub Releases 호스팅 유지** ③ 코드 서명은 OSS 무료 경로 선호하되 선택 ④ Windows 전용 WPF .NET 8 self-contained.

### 2.1 업데이트 전달 메커니즘

| 후보 | 델타/백그라운드 | 무료 GitHub 호스팅 | 서명 요구 | self-contained WPF | 평가 |
|------|----------------|-------------------|-----------|---------------------|------|
| **Velopack** | ✅ 델타 + 백그라운드 스테이징 + 1-클릭 재시작 | ✅ 1급 지원 | 선택(무서명 동작) | ✅ | **추천.** Rust 코어, Squirrel 후계, 멀티플랫폼, API 단순(`CheckForUpdatesAsync`/`DownloadUpdatesAsync`/`ApplyUpdatesAndRestart`), UI는 직접 제어 |
| Clowd.Squirrel | ✅ | ✅ | 선택 | ✅ | 사실상 Velopack에 흡수·마이그레이션 권장됨. 신규 채택 비추천 |
| MSIX + App Installer | 부분(블록맵 델타) | △(appinstaller XML 호스팅 가능하나 까다로움) | **❌ 서명 필수** | △ 패키징 제약 | 서명 선택 제약과 충돌. 무서명 OSS엔 부적합 |
| WinGet | ✅(매니페스트) | △(별도 매니페스트 PR/저장소) | 사실상 서명 권장 | ✅ | 배포 채널이지 인앱 자동 업데이트 프레임워크 아님. 보조 채널로만 |
| Inno Setup 유지 + 자체 개선 | ❌ 매번 전체 exe | ✅ | 선택 | ✅ | 현행. 델타·백그라운드 부재로 "현대적 유연함" 목표 미달 |

**선정: Velopack.** 무료 호스팅·무서명 동작·델타·백그라운드 스테이징·self-contained WPF 지원을 동시에 만족하는 유일 후보. UI를 직접 제어할 수 있어 아래 알림 재설계와 결합 가능.

### 2.2 알림(발견성) 채널

| 후보 | 휘발성 | OS 억제 내성 | 비고 |
|------|--------|-------------|------|
| 레거시 NotifyIcon balloon (현행) | 휘발 | 약함 | 만료 후 흔적 없음 |
| **WinRT 토스트(`ToastNotificationManagerCompat`)** | 반(Action Center 누적) | 중간 | Win32/WPF 무패키징 OK, 최신판은 바로가기 불필요, Action Center에 남음. 단 OS 억제는 여전히 적용. **주의: CommunityToolkit 저장소는 2026-02-25 아카이브(read-only)** → 신규는 Windows App SDK `AppNotificationManager`가 전방 경로 |
| **트레이 아이콘 배지/오버레이** | 비휘발 | **강함** | 알림 정책과 무관하게 항상 보임 |
| **런처 내 인앱 배너** | 비휘발 | **강함** | 사용자가 런처를 열면 100% 노출 |

**선정: 다중 표면.** 토스트(WinRT, best-effort) + 트레이 배지 + 런처 인앱 배너를 함께 사용. 토스트가 억제돼도 트레이/런처에서 반드시 발견됨.

### 2.3 코드 서명

| 옵션 | 비용 | publisher 표기 | 권고 |
|------|------|---------------|------|
| 무서명 | 무료 | 없음 → SmartScreen 경고(평판 누적 전) | 단기 허용. Velopack은 무서명 동작 |
| **SignPath Foundation (OSS)** | **무료** | "SignPath Foundation" | **OSS 선호에 부합 → 1순위 도입 후보.** 자동 빌드 출처 검증, HSM 보관 |
| Azure Artifact Signing | ~$9.99/월 | 본인/조직 | 유료지만 단기 인증서·자동 타임스탬프. 예산 생기면 차선 |

**판단(직접):** **무서명으로 시작하되 SignPath Foundation OSS 무료 서명을 병행 도입 목표.** 무서명이라도 Velopack 업데이트 자체는 동작하므로 기능은 즉시 확보되고, SignPath로 SmartScreen 평판 문제를 무료로 점진 해소한다. MSIX처럼 서명을 *전제*하는 경로는 배제(제약 충돌).

---

## 3. 재설계된 To-Be UX 흐름

```
[시작 30s 후 / 24h 주기]
   │
   ├─ Velopack UpdateManager.CheckForUpdatesAsync()  ── 없음 → 조용히 종료(기존 원칙 유지)
   │
   └─ 있음 →  ① WinRT 토스트 1회 시도(best-effort, 클릭 시 What's New)
              ② 트레이 아이콘에 "업데이트" 배지/메뉴 항목 상시 표시   ← OS 억제와 무관
              ③ 다음 런처 소환 시 상단 인앱 배너 노출               ← 발견성 보장
                   │
                   └─ 사용자가 "지금 업데이트" 클릭
                          │
                          ├─ DownloadUpdatesAsync(progress)   ← 델타, 백그라운드
                          └─ ApplyUpdatesAndRestart()         ← 1-클릭 적용 + 재시작
```

핵심 차이: **발견성은 비휘발 표면(트레이 배지 + 인앱 배너)이 보장**하고, 토스트는 "즉시성"을 위한 보너스로만 쓴다. 적용은 Inno 사일런트 실행 대신 Velopack 델타/재시작으로 매끄럽게.

---

## 4. 마이그레이션 개요 (현재 코드 → 권고안)

> 보류 컴포넌트(구현)는 별도 승인 후 진행. 아래는 *실행 계획 초안*.

1. **패키징 전환** — `installer/blink.iss`(Inno) → Velopack `vpk` 빌드로 교체. `.github/workflows/release.yml`에서 `vpk pack`/`vpk upload github`로 릴리스 자산 생성(델타 포함). `latest.json` 매니페스트는 Velopack 피드(`RELEASES`/`releases.{channel}.json`)로 대체 가능 여부 검토.
2. **Core 정리** — `Blink.Core/Update/UpdateChecker.cs`의 수동 GitHub 폴링/SemVer 비교를 Velopack `UpdateManager`(GitHub source)로 대체. `ReleaseInfo`/`UpdatePolicy`는 Velopack `UpdateInfo`로 매핑하거나 얇은 어댑터 유지.
3. **App 서비스 교체** — `UpdateService`의 `DownloadInstallerAsync`/`LaunchInstaller`/`CleanupTempInstallers`를 `DownloadUpdatesAsync` + `ApplyUpdatesAndRestart`로 대체(`%TEMP%` 수동 관리·잔여 정리 제거). `DispatcherTimer` 30s/24h 케이던스는 유지.
4. **알림 재설계** — `App.xaml.cs:217 OnUpdateAvailable`의 `ShowBalloonTip` 제거. 대체:
   - WinRT 토스트: 신규는 Windows App SDK `AppNotificationManager` 권장(CommunityToolkit 아카이브됨). AUMID 등록.
   - 트레이: `NotifyIcon` 메뉴/아이콘에 "업데이트 있음" 상시 항목 추가.
   - 런처: `LauncherWindow`/`LauncherViewModel`에 인앱 업데이트 배너 상태 추가, `Summon` 시 노출.
5. **서명** — 무서명으로 1차 릴리스 → SignPath Foundation OSS 신청·CI 연동(자동 빌드 출처 검증) → publisher "SignPath Foundation"로 SmartScreen 평판 누적.
6. **검증** — `Blink.App`는 Windows 전용 빌드이므로 Velopack 통합·토스트·재시작 흐름은 **Windows 환경에서 실측 필요**(macOS 빌드 불가). 사전 릴리스 태그(`vX.Y.Z-rc1`)로 전체 파이프라인 드라이런.

### 리스크 / 주의
- CommunityToolkit.Notifications 아카이브(2026-02): 신규 의존은 Windows App SDK 쪽으로.
- Velopack은 설치 위치/부트스트랩 모델이 Inno와 다름 → 기존 설치 사용자 **마이그레이션 경로(1회 전환 인스톨러)** 별도 설계 필요.
- 무서명 구간 동안 SmartScreen 경고 → 릴리스 노트/README에 안내.

---

## 5. Sources
- [Velopack — 공식 사이트](https://velopack.io/) · [문서](https://docs.velopack.io/) · [GitHub](https://github.com/velopack/velopack)
- [Velopack UpdateManager API (CheckForUpdatesAsync / ApplyUpdatesAndRestart)](https://docs.velopack.io/reference/cs/Velopack/UpdateManager)
- [Clowd.Squirrel (→ Velopack 마이그레이션)](https://github.com/clowd/Clowd.Squirrel)
- [Microsoft Learn — Send local toast from unpackaged apps (AUMID/COM activator)](https://learn.microsoft.com/en-us/windows/apps/develop/notifications/app-notifications/send-local-toast-other-apps)
- [CommunityToolkit ToastNotificationManagerCompat (Win32 무바로가기)](https://learn.microsoft.com/en-us/dotnet/api/communitytoolkit.winui.notifications.toastnotificationmanagercompat)
- [SignPath Foundation — OSS 무료 코드 서명](https://signpath.io/solutions/open-source-community) · [조건](https://signpath.org/terms.html)
- [Azure Artifact Signing(구 Trusted Signing)](https://azure.microsoft.com/en-us/products/artifact-signing)
- [Microsoft Learn — Code signing options for Windows](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/code-signing-options)
