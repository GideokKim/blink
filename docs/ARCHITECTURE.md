# Blink — 프로그램 상세 설명

Blink는 Windows 데스크톱 런처 + 로컬 파일 검색 앱이다. 등록한 폴더를 인덱싱해 SQLite
FTS DB에 저장하고, 핫키로 띄운 런처에서 빠르게 검색·실행한다.

이 문서는 프로그램 전체 구조를 설명한다. 개별 기능의 깊은 설계는 [`docs/specs/`](specs/)에
분리해 두고 해당 섹션에서 링크한다.

## 솔루션 구성 (`Blink.sln`)

| 프로젝트 | 역할 | 빌드/테스트 |
|---|---|---|
| **Blink.Core** | 플랫폼 무관 라이브러리: 인덱싱, 저장(SQLite FTS), 검색, 파서, 설정, 업데이트 로직 | macOS/Win 모두 `dotnet test` |
| **Blink.App** | WPF UI(런처·설정·트레이·테마·업데이트 창). **Windows 전용** | CI(Windows)에서만 빌드 |
| **Blink.Cli** | Core 위의 헤드리스 하니스(`index`/`search`/`status`/`prune`). 비Windows 로컬 검증용 | 로컬 |
| **Blink.Indexer.Worker** | 파일 I/O를 전담하고 저장 연산을 JSONL로 흘려보내는 **별도 워커 실행파일**(out-of-process 인덱싱 경로) | 로컬/테스트 |
| **Blink.Core.Tests** | Core의 xUnit 테스트 | macOS/Win |

> **검증 제약**: `Blink.App`은 WPF·Windows 전용이라 macOS에서 빌드/실행 불가. 순수 로직은
> `Blink.Core`에 두어 단위 테스트하고, App 와이어링은 코드 리뷰와 CI 빌드로만 확인한다.

## 데이터 흐름

```
config.json(folders[]) ──▶ 인덱싱 ──▶ SQLite FTS DB(index.db) ──▶ 검색 ──▶ 런처 UI
```

## 설정 (`Blink.Core/Config/AppConfig.cs`)

`%APPDATA%/Blink/config.json`에 영속화. 주요 필드: `folders[]`(등록 인덱싱 루트),
`db_path`, `autostart`, 테마/액센트(`theme_mode`/`base_color`/`accent`),
`auto_index_interval`, `folder_index_times`(루트별 마지막 인덱싱 시각),
`update_check`/`skip_version`/`last_seen_version`. `Load()`가 구 필드(`theme`/`accent_hue`)를
신 필드로 일회성 마이그레이션한다.

## 인덱싱 (`Blink.Core/Indexing`)

같은 순회/증분/번들 의미론을 공유하는 **두 구현**이 있다.

1. **in-process — `Indexer.Index`**: `IndexingService`(App)와 `Blink.Cli`가 사용. **현재
   App이 실제로 쓰는 경로.**
2. **out-of-process — `WorkerIndexer.Run`**(`Blink.Indexer.Worker` 실행파일): 모든 파일
   I/O를 워커 프로세스에서 수행하고 upsert/delete 연산을 JSONL로 스트리밍, `WorkerIndexClient`가
   구동. **현재 테스트로만 구동되며 App엔 미연결.**

### `Indexer` 3-패스 구조

대규모 트리(한 트리에 수백만 파일)에서도 피크 메모리를 작게 유지하기 위해 디스크 기반
`ScanCache`를 3번 통과한다.

1. **scan** — 디렉토리 1회 순회, 제외 안 된 경로를 디스크로 흘려보냄(아직 stat·DB 쓰기 없음).
2. **plan** — 스캔을 스트리밍하며 (폴더, 확장자)별 번들 후보 수 집계.
3. **apply** — 다시 스트리밍하며 개별 파일을 증분 upsert, 큰 동질 그룹을 번들 마커로 축약.

핵심 특성:
- **증분**: 저장된 `(doc_id, mtime)`를 한 번 읽어 mtime이 같은 파일은 스킵.
- **번들링**: 콘텐츠 파서가 없는 파일(이미지/데이터)이 같은 폴더·확장자로 임계치(기본 100)
  이상 쌓이면 `<dir>/__bundle__<ext>` 가상 문서 1개로 축약. 콘텐츠 있는 파일은 번들 안 함.
- **제외**(`FileExcluder`): 내장 기본 패턴(`~$*`, `.git/`, `node_modules/`, `.DS_Store`,
  `$RECYCLE.BIN/` 등) + 루트의 `.blinkignore`(gitignore 유사 글롭). 대소문자 무시.
- **본문 추출 상한**: 파서가 콘텐츠를 읽고 크기가 전역 100MB 및 파서별 상한 이내일 때만 본문
  추출, 아니면 파일명만 색인.

### 가지치기 (`Pruner`)

인덱싱 후 디스크에서 사라진 파일의 인덱스 항목을 제거한다. 오직 `!File.Exists`(번들은
`!Directory.Exists`)로만 삭제하므로 멱등·순서 무관. 루트가 접근 불가면
`RootUnavailableException`을 던져 가지치기를 건너뛴다 — NAS/마운트가 잠깐 끊겼다고 인덱스
전체가 삭제되는 것을 막는 안전장치.

### 루트 확장 — 적응형 청킹

등록한 루트(특히 드라이브 루트·NAS 공유)는 단일 루트로 통째 인덱싱하지 않고, 내부 하위
폴더 단위의 **청크**로 적응형 분할해 각 청크를 독립적으로 인덱싱·가지치기한다. 목적은
**중간 취소·종료 시 손실 최소화**(완료된 청크는 커밋·가지치기까지 끝나 보존됨). 청크 분할은
순수 런타임 결정이라 DB에 영속화되지 않으며, 기존 DB·설정과 완전 호환된다.

> 상세 설계(알고리즘, passage 터널링, `Indexer.Index` 시그니처 분리, 호환성 분석, 테스트
> 전략): **[`docs/specs/adaptive-root-chunking.md`](specs/adaptive-root-chunking.md)**

`DriveSplit`은 드라이브 루트를 즉시 하위 폴더로 펼쳐 마운트 격리(한 공유가 끊겨도 나머지
진행, 가지치기 가드가 자식별 적용)를 제공한다. 적응형 청킹은 이를 일반화한다.

### 자동 인덱싱 (`AutoIndexScheduler` / `AutoIndexInterval`)

`auto_index_interval`(`15m`/`1h`/`6h`/`off`)에 따라 주기적으로 재인덱싱을 트리거. 진행 중인
실행이 있으면(`IsBusy`) 취소·재시작하지 않는다.

## 저장 (`Blink.Core/Store/SqliteFtsStore.cs`)

- `documents` 테이블(`doc_id`=파일 절대경로 UNIQUE, `path`, `mtime`, `size`, `content`,
  `is_bundle`, `member_count`) + FTS5 가상 테이블 `documents_fts`(`tokenize='unicode61'`).
- `schema_meta.version`로 스키마 버전 관리, `Migrate()`가 구 DB에 신 컬럼을 추가(`CREATE …
  IF NOT EXISTS` + `ALTER`). 신규 DB는 `InitSchema()`로 현재 스키마 생성.
- WAL 저널, `busy_timeout`. 쓰기는 한 게이트로 직렬화, 읽기는 별도 게이트.
- `IterDocsUnder(root)`/`FolderStats(root)`는 `doc_id=$p OR doc_id LIKE prefix+sep+'%'`
  **경로 프리픽스 쿼리**. 검색은 `bm25(documents_fts)` 순위.

## 토큰화·파싱 (`Blink.Core/Tokenization`, `Parsers`)

- `NgramTokenizer` — 부분 일치 검색을 위한 n-gram 토큰 생성(FTS `tokens` 컬럼에 저장).
- `ParserRegistry` — 확장자 → `IParser` 매핑: `Text`/`Pdf`/`Docx`/`Xlsx`/`Pptx`/`Hwpx`/
  `Rtf`/`FilenameOnly`. 각 파서는 `ReadsContent`·`MaxParseSize`로 추출 가부·상한을 알린다.

## 검색 (`Blink.Core/Search`, `Launch`)

- `InProcessProvider` — store 위에서 FTS 질의를 수행하는 검색 제공자(`ISearchProvider`).
- `SearchCoordinator`/`SearchPolicy` — 질의 디바운스·정책. `LaunchSearch`/`HitToLaunchItem` —
  검색 히트를 실행 가능한 런처 항목으로 변환. `ArithmeticEvaluator` — 계산기형 즉답.

## 업데이트 (`Blink.Core/Update`, `Blink.App/Update`)

- `UpdateChecker`/`UpdatePolicy`/`SemVer`/`ReleaseInfo` — CDN의 `latest.json` 매니페스트로
  새 릴리스 확인(GitHub API rate limit 회피). `update_check`·`skip_version` 설정 반영.
- App 측 `UpdateService`/`UpdateWindow`/`WhatsNewWindow` — 업데이트 안내·"새로워진 점" 표시,
  `MarkdownLite`/`MarkdownView`로 릴리스 노트 렌더.

## App (`Blink.App`)

- `App.xaml.cs` — 부트스트랩: store·`IndexingService`·트레이·전역 핫키(`HotkeyHook`)·자동
  인덱싱·업데이트 체크 구성. 인덱싱 진행/완료를 UI로 마샬링하고, 완료 시 `folder_index_times`
  플러시.
- `LauncherWindow` + `ClassicView`(방향 A) / `SplitView`(방향 B) — 런처 레이아웃.
- `SettingsWindow` — 폴더 추가/삭제, 폴더별 통계(`{n}파일 · {size} · 마지막 인덱싱 …`),
  테마/액센트, 자동 인덱싱 주기.
- 테마: `Theming/Oklch`·`ThemeManager`(값 기반 base color + 액센트, 시스템 다크/라이트 추종).
- MVVM: `Mvvm/ObservableObject`, `ViewModels/*`.

## 빌드/릴리스

`v*` 태그 푸시 → `.github/workflows/release.yml`이 Windows 설치파일을 CI에서 빌드(SignPath
서명). CI가 App 레이어 빌드 게이트다.
