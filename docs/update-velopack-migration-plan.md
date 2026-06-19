# Velopack 전환 구현 계획 + 기존 설치 마이그레이션 분석

> 작성일: 2026-06-19 · 선행 리서치: `docs/update-ux-research.md`
> 범위: 업데이트 전달 메커니즘(Inno Setup → Velopack) 구현 계획 + "기존 앱에서 자연스러운 전환 가능성" 분석
> 검증 제약: `Blink.App`은 Windows 전용(macOS 빌드 불가) → 모든 통합·전환은 **Windows 실측 필요**

---

## Part A. 기존 앱에서 자연스럽게 업데이트되나? (핵심 분석)

### 결론 먼저
**자동으로는 안 된다. 단, "브리지 릴리스" 한 번을 우리가 정확히 만들면 사용자 입장에선 자연스러운 1회 전환이 가능하다.**

Velopack의 자동 마이그레이션은 **Squirrel.Windows / Clowd.Squirrel 전용**이다. Inno Setup → Velopack 자동 전환 경로는 **없다**. Velopack은 자기가 설치하지 않은 앱을 업데이트하지 못하므로, 전환은 우리가 만들어야 한다.

### 현재 설치 모델 vs Velopack (충돌 지점)

| 항목 | 현재 (Inno, `installer/blink.iss`) | Velopack | 충돌 |
|------|-----------------------------------|----------|------|
| 설치 위치 | `%LocalAppData%\Programs\Blink` (`{autopf}` + `PrivilegesRequired=lowest`) | `%LocalAppData%\{packId}\current\` | **다름 → 정리 안 하면 이중 설치** |
| 권한 | per-user (관리자 불필요) | per-user | ✅ 동일 — 유리 |
| 자동시작 | HKCU Run → `"{app}\Blink.App.exe"` (`blink.iss:64`) | Velopack이 shortcut/Run 자동 관리 | 옛 Run 키 잔존 시 중복/깨진 자동시작 |
| 제거 항목 | Inno가 HKCU Uninstall 등록 | Velopack 자체 관리 | 옛 uninstall 엔트리 잔존 |
| 진입점 | WPF 자동 생성 Main | `VelopackApp.Build().Run()`를 Main **최초**에 호출 필수 | 코드 변경 필요 |
| 패키징 | `PublishSingleFile=true` 단일 exe | `vpk`가 publish **폴더**를 pack | 단일파일 해제 권장 |
| 워커 | `Blink.Indexer.Worker.exe` 동봉(`blink.iss:55`) | 패키지에 추가 파일로 포함, `--mainExe Blink.App.exe` | 번들 + 경로 해석 확인 |
| 실행 중 프로세스 | 트레이 상주 | — | 전환 시 파일 잠금 → 선(先)종료 필요 |

### 유리한 점 (자연스러운 전환을 가능케 하는 요소)
1. **둘 다 per-user** → 관리자 권한 프롬프트 없이 전환 가능.
2. **`AutostartManager`가 `Environment.ProcessPath` 사용** (`Blink.App/Interop/AutostartManager.cs:26`) → 실행 위치가 바뀌어도 자동시작 경로가 **자기수정**된다. 새 위치에서 한 번 켜지면 Run 키가 새 경로로 갱신됨.
3. **이미 동작하는 인앱 업데이트 경로**: 현재 앱은 `latest.json`의 `installerUrl`에서 받은 exe를 `/SILENT /SUPPRESSMSGBOXES /NORESTART`로 실행한다(`UpdateService.cs:126`). → 이 메커니즘을 **그대로 재사용해 브리지 인스톨러를 배달**할 수 있다. 사용자는 평소처럼 "업데이트" 한 번만 누르면 된다.

### 권장 전환 전략 — "브리지 릴리스" (1회)
마지막 Inno 기반 릴리스를 *전환 전용 브리지 인스톨러*로 만든다. 기존 사용자가 평소대로 업데이트 알림→설치를 하면, 이 인스톨러가:

```
1. 실행 중 Blink.App.exe / Blink.Indexer.Worker.exe 종료
2. 구 Inno 설치 정리:
     - %LocalAppData%\Programs\Blink 파일 삭제 (또는 옛 unins000.exe /SILENT 실행)
     - HKCU\...\Run 의 "Blink" 값 삭제
     - HKCU Uninstall 엔트리 삭제
3. 번들된 Velopack Setup(Blink-win-Setup.exe)을 silent 실행
     → %LocalAppData%\Blink\current\ 에 설치, Velopack이 shortcut/Run 생성
4. 새 앱 실행
```

이후 **모든 업데이트는 Velopack `UpdateManager`**(델타·백그라운드 스테이징·1-클릭 재시작)가 처리하고, Inno/`installerUrl` 경로는 폐기한다.

→ 사용자 체감: "한 번 업데이트 누름 → 잠깐의 전환 설치 → 이후로는 매끄러운 자동 업데이트". **자연스럽다고 부를 수 있는 수준**이지만, 그 1회는 우리가 브리지를 정확히 구현·검증해야만 성립한다.

**대안(비권장):** README/릴리스 노트로 "수동 재설치" 공지. 구현은 단순하나 "자연스러움" 목표에 미달, 사용자 이탈 위험. → 브리지 방식 권장.

---

## Part B. 구현 계획 (단계별, 커밋 단위)

> 커밋은 한 논리 변경 = 한 커밋. 타입 혼합 금지(`feat`/`build`/`refac`/`docs`/`test` 분리).

### Phase 0 — 스파이크 & 사전 검증 (Windows)
- [ ] `vpk` CLI 설치(`dotnet tool install -g vpk`), `Velopack` NuGet을 `Blink.App`에 추가.
- [ ] 최소 스파이크: 더미 빌드를 `vpk pack` → 로컬 설치 → 자동 업데이트 1회가 도는지 확인.
- [ ] 워커 번들/경로 해석 확인: `vpk pack` 출력에 `Blink.Indexer.Worker.exe` 포함, `current/` 교체 후에도 `AppContext.BaseDirectory` 기준 상대 경로로 워커가 실행되는지 검증(`WorkerIndexClient.IndexViaWorker`에 넘기는 경로를 base-dir 상대로 통일).

### Phase 1 — 앱 Velopack 통합 (`feat`)
- [ ] **진입점**: `Program.cs`에 `[STAThread] static void Main()` 추가 → 첫 줄 `VelopackApp.Build().Run();` 후 WPF `App` 부팅. `Blink.App.csproj`에 `<StartupObject>Blink.App.Program</StartupObject>` 설정(자동 생성 Main 비활성).
- [ ] **패키징 모드**: `PublishSingleFile`/`IncludeNativeLibrariesForSelfExtract` 제거(폴더 publish로). self-contained 유지.
- [ ] **업데이트 로직 교체**: `UpdateService`의 `FetchLatestAsync`/`DownloadInstallerAsync`/`LaunchInstaller`/`CleanupTempInstallers`를 Velopack `UpdateManager`(`CheckForUpdatesAsync` → `DownloadUpdatesAsync(progress)` → `ApplyUpdatesAndRestart`)로 대체. 기존 30초/24시간 `DispatcherTimer` 케이던스와 `update_check` 토글은 유지.
- [ ] **Core 정리**(`refac`): `Blink.Core/Update/UpdateChecker.cs`의 수동 GitHub 폴링/`latest.json` 파싱을 제거하거나 Velopack feed 어댑터로 축소. `UpdatePolicy.ShouldOffer`/`SkipVersion`은 Velopack `UpdateInfo` 기준으로 재배선.
- [ ] **알림 연계**: `App.xaml.cs:217`의 `ShowBalloonTip` 제거 → 리서치 권고대로 다중 표면(트레이 배지 + 인앱 배너 + best-effort 토스트). *(알림 컴포넌트는 별도지만 이 Phase에서 진입점만 맞춰둠.)*

### Phase 2 — 릴리스 파이프라인 (`build`/`ci`)
- [ ] `.github/workflows/release.yml` 개편:
  - `dotnet publish`를 폴더 출력으로(단일파일 플래그 제거).
  - `vpk pack --packId Blink --packVersion <ver> --mainExe Blink.App.exe --packDir <publish>` 로 패키지/Setup/델타 생성.
  - `vpk upload github` 로 GitHub Release에 Velopack 자산(`releases.win.json`, `*-full.nupkg`, `*-delta.nupkg`, `Blink-win-Setup.exe`) 업로드.
- [ ] **서명 재배치**(`ci`): 현재 "exe 서명 → Inno 빌드 → 인스톨러 서명" 흐름을 "publish 산출 exe 서명 → `vpk pack` → vpk가 만든 Setup 서명"으로 재배치. SignPath 연동(`SIGNPATH_API_TOKEN`)은 이미 존재하므로 슬롯만 교체.
- [ ] `latest.json` 수동 생성 단계는 Velopack feed로 대체(또는 브리지 기간 병행).

### Phase 3 — 브리지 릴리스 (1회, `build`)
- [ ] 전환 전용 Inno 스크립트(`installer/blink-bridge.iss`) 작성: 구 설치 정리 + 번들된 Velopack Setup silent 실행(Part A 4단계).
- [ ] 직전 안정 버전 `latest.json`의 `installerUrl`을 **브리지 인스톨러**로 지정(기존 인앱 업데이터가 이걸 받아 실행).
- [ ] **드라이런 검증**: 사전 릴리스 태그(`vX.Y.Z-rc1`)로 전체 파이프라인 + *구 버전이 설치된 Windows VM*에서 end-to-end 전환 실측(이중 설치/자동시작 중복/워커 동작 확인).

### Phase 4 — 정리 (`refac`/`docs`)
- [ ] `installer/blink.iss`(상시 Inno) 및 `UpdateService`의 다운로드/사일런트 실행 코드 제거.
- [ ] `RELEASING.md`, `README*.md`, `BUILD-WINDOWS.md`를 Velopack 흐름으로 갱신.

---

## 리스크 & 검증 포인트
- **Windows 전용 실측**: Velopack 통합·브리지 전환·토스트·재시작은 macOS에서 검증 불가. 구 버전 VM에서 전환 시나리오 반드시 재현.
- **이중 설치 회귀**: 브리지가 구 설치 정리를 누락하면 두 개의 Blink(두 트레이/두 자동시작)가 공존. 정리 단계가 가장 깨지기 쉬움 → 집중 테스트.
- **워커 EDR 격리**: `current/` 통째 교체 후에도 워커 상대 경로 해석이 유지되는지 확인.
- **서명 공백 구간**: 무서명 → SignPath Foundation 평판 누적 전까지 SmartScreen 경고 가능 → 릴리스 노트 안내.
- **롤백 계획**: 브리지 릴리스에 문제가 생기면 `latest.json` `installerUrl`을 직전 Inno 인스톨러로 즉시 되돌릴 수 있게 유지.

## Sources
- [Velopack — Integrating Overview (설치 레이아웃 `%LocalAppData%\{packId}\current\`, `VelopackApp.Build().Run()`)](https://docs.velopack.io/integrating/overview)
- [Velopack — UpdateManager API](https://docs.velopack.io/reference/cs/Velopack/UpdateManager)
- [Velopack — Migrating from Squirrel (자동 마이그레이션은 Squirrel 계열 전용)](https://docs.velopack.io/migrating/squirrel)
- [Velopack — Installers / Distributing](https://docs.velopack.io/packaging/installer)
