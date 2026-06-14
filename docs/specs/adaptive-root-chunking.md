# 적응형 루트 청킹 (Adaptive root chunking)

> 상위 문서: [`docs/ARCHITECTURE.md`](../ARCHITECTURE.md) › 인덱싱 › 루트 확장
> 상태: 설계 확정, 구현 진행
> 날짜: 2026-06-13

## 목표

등록된 인덱싱 루트(특히 드라이브 루트·NAS 공유)를 **단일 루트로 통째 인덱싱하지 않고**,
내부 하위 폴더 단위의 여러 "청크"로 적응형 분할해 인덱싱한다. 목적은 **중간 취소·프로그램
종료 시 손실 최소화**다.

`Indexer`는 3-패스(scan → plan → apply) 구조라, 한 루트의 scan(전체 트리 순회)이 끝나기
전에는 단 한 건도 DB에 커밋하지 않는다(`Indexer.cs:58-146`). 수백만 파일 드라이브를 단일
루트로 주면 scan 도중 종료 시 진행분이 0이다. 루트를 하위 청크로 쪼개면 **완료된 청크는
커밋+가지치기까지 끝나** 중단돼도 보존되고, 재실행 시 그 청크는 scan조차 다시 하지 않는다.

## 제약

- `Blink.App`은 WPF·Windows 전용 → macOS 빌드/실행 불가. 순수 로직은 `Blink.Core`에 두어
  `dotnet test`로 검증, App 와이어링은 코드 리뷰.
- **확장(Expand)은 `IndexingService.ReindexAsync`의 등록-폴더 루프 *안쪽*에서** 수행한다.
  App 진입부에서 청크를 펼쳐 `ReindexAsync`에 넘기면 `FolderCompleted`가 청크 단위로
  발화하여 `config.json`의 `folder_index_times` 키잉(등록 루트 기준)이 깨진다
  (`App.xaml.cs:61,116`). 등록 폴더당 `FolderCompleted`는 **정확히 1회** 발화해야 한다.
- **DB 호환 불변식**: 청크 분할은 순수 런타임 순회 결정이며 **DB에 영속화되지 않는다**.
  `doc_id = 파일 절대경로`, 번들 마커 `<dir>/__bundle__<ext>` 형식 불변. 스키마 변경 없음.

## 적용 범위

- **대상**: in-process 경로 — `IndexingService` → `Indexer.Index`(App이 실제 사용) 및
  `Blink.Cli`.
- **범위 밖(후속)**: out-of-process 워커 경로(`WorkerIndexer.Run` /
  `Blink.Indexer.Worker`)는 동일한 3-패스 문제를 갖지만 **현재 App에 미연결**이라 이번
  변경에서 제외한다. 본 설계의 `Indexer.Index` 분리 시그니처는 재사용 가능한 1차 부품이며,
  워커 경로도 추후 같은 `Expand`를 채택하면 된다. 워커를 연결할 때 함께 처리한다.

## 1. 적응형 Expand 알고리즘 (`Blink.Core/Indexing`)

`DriveSplit.Expand`(현재 문자열 리스트 반환, 드라이브 루트만 1단계 분할)를 청크 리스트를
반환하는 적응형 버전으로 확장한다.

### 반환 형태

```csharp
public readonly record struct RootChunk(string EnumRoot, bool Recursive);
IReadOnlyList<RootChunk> Expand(string configuredRoot);
```

- `EnumRoot`: 열거 시작 폴더(청크 경로).
- `Recursive`: `true`면 서브트리 전체(`AllDirectories`), `false`면 직속 파일만
  (`TopDirectoryOnly`).
- 접근 불가/없는 루트 → 빈 리스트(현재 동작 유지).

### 알고리즘

```
Expand(configuredRoot):
  존재 안 함 → []
  드라이브 루트 → 안전상 무조건 1단계 강제 분할(현재 마운트 격리 동작 보존):
                  if 드라이브 직속 파일 > 0: emit(driveRoot, recursive=false)  // 루트 직속 파일 누락 방지
                  각 자식에 adaptive(child, depth=1, absDepth=1)
  아니면        → adaptive(configuredRoot, depth=0, absDepth=0)

adaptive(dir, depth, absDepth):
  if absDepth >= AbsMaxDepth:  emit(dir, recursive=true); return   // 심링크 루프 안전 가드
  subdirs = GetDirectories(dir)                                    // 한 번의 싼 listing
  files   = GetFiles(dir).Length                                   // 직속 파일 수
  if subdirs.Length == 0:      emit(dir, recursive=true); return   // 진짜 leaf
  if depth >= MaxDepth:        emit(dir, recursive=true); return   // 분기 깊이 cap → 나머지 흡수

  isPassage = (subdirs.Length == 1 && files <= K)   // 얇은 통과 폴더
  nextDepth = isPassage ? depth : depth + 1          // ★ passage는 분기 깊이 예산 0 소비

  if files > 0: emit(dir, recursive=false)           // dir 직속 흩어진 파일 담당
  foreach sub in subdirs: adaptive(sub, nextDepth, absDepth + 1)
```

### 핵심 불변식

- **멈춤(emit) = `recursive=true` = 남은 서브트리 전체 커버.** 어디서 멈추든(leaf,
  MaxDepth, AbsMaxDepth) 그 아래 모든 파일을 한 청크가 흡수 → **파일 누락 0**.
- **파티션 성질**: 한 폴더의 직속 파일은 정확히 한 청크가 열거한다(내려가면
  `recursive=false`가 직속 파일, 멈추면 `recursive=true`가 자신 포함 서브트리). 청크 집합은
  트리의 분할(disjoint + covering) → **이중 인덱싱 0**, 번들 카운트는 단일루트와 동일.
- **passage 터널링**: 얇은 체인(단일 서브·파일 ≤ K)은 분기 깊이 예산을 소비하지 않고
  통과한다. `MaxDepth`는 "경로 깊이"가 아니라 "의미 있는 분기 깊이"를 잰다. NAS의
  `share/a/b/c/d/e`처럼 위는 얇고 바닥에 내용이 몰린 구조에서도 얇은 구간을 깊이 비용 0으로
  내려가 실제 분기/내용 레벨에서 청킹한다.

### 상수 (내부 const, 설정 UI 노출 안 함)

| 상수 | 값 | 의미 |
|---|---|---|
| `K` | `8` | passage 판정용 직속 파일 허용치(통과 폴더는 파일이 거의 없어야 함) |
| `MaxDepth` | `3` | 분기 깊이 cap(드라이브 루트는 강제 1단계 후 여기서부터 적응) |
| `AbsMaxDepth` | `64` | 절대 재귀 깊이 안전 가드(심링크 루프·스택오버플로 차단) |

## 2. `Indexer.Index` 시그니처 변경 (`Blink.Core/Indexing/Indexer.cs`)

현재 한 `root` 인자가 (열거 시작, 제외 기준, 재귀 여부)를 모두 겸한다. 세 역할을 분리한다.

```csharp
// 신 시그니처
public void Index(
    string enumRoot,        // 열거 시작 폴더 (= 청크 경로)
    string excludeRoot,     // 제외 규칙 기준 (= 사용자 등록 루트)
    bool recursive,         // false면 TopDirectoryOnly
    IIndexStore store, IProgress<IndexProgress>? progress, CancellationToken ct)

// 구 시그니처는 위임 오버로드로 보존(기존 테스트·CLI 비분할 경로 보호)
public void Index(string root, IIndexStore store, IProgress<IndexProgress>? progress, CancellationToken ct)
    => Index(root, root, recursive: true, store, progress, ct);
```

내부 변경점 3곳:

1. `FileExcluder.ForRoot(excludeRoot)` + `IsExcluded(path, excludeRoot)` — **제외 의미론을
   등록 루트에 고정**. `.blinkignore`와 루트-상대 경로 패턴은 청크가 깊어져도 등록 루트
   기준으로 평가된다(분할이 *무엇이* 인덱싱되는지를 바꾸지 않음).
2. `Directory.EnumerateFiles(enumRoot, "*", recursive ? AllDirectories : TopDirectoryOnly)`.
3. `IterDocsUnder(enumRoot)`, 번들 키 계산(`rootFull`)은 `enumRoot` 기준 유지. 프리픽스
   쿼리·파티션 성질 그대로 성립.

### 비재귀 청크의 over-match 안전성

`IterDocsUnder(enumRoot)`는 프리픽스 쿼리라 비재귀 청크에서 하위 폴더 doc를 over-match한다.
그러나 (a) `known` 사전에 들어갈 뿐 삭제는 **열거된 경로**에서만 파생되므로 교차 청크 삭제가
없고, (b) 가지치기는 오직 `!File.Exists`로만 삭제하므로 over-match는 존재하는 파일을 건드리지
않는다(멱등·순서 무관). 안전.

## 3. `IndexingService.ReindexAsync` (`Blink.Core/Indexing/IndexingService.cs`)

등록-폴더 루프 안쪽에서 Expand:

```
foreach (folder in folderList):
    chunks = Expand(folder)            // 루프 안쪽
    foreach (chunk in chunks):
        Index(chunk.EnumRoot, folder, chunk.Recursive, store, progress, ct)
        try { pruner.Apply(chunk.EnumRoot, store) }
        catch (RootUnavailableException) { /* 이 청크만 prune 스킵 */ }
    FolderCompleted?.Invoke(folder)    // 등록 폴더당 1회
```

- `Index`의 `excludeRoot`는 항상 `folder`(등록 루트).
- 가지치기는 청크 단위 → `RootUnavailableException` 가드가 청크별 적용(형제 청크 미영향).
- `FolderCompleted`는 청크가 아니라 **등록 폴더**로 1회 발화 → `folder_index_times` 키잉
  보존, 기존 `config.json`과 완전 호환.

## 4. 호출부 갱신

- `Blink.Cli/Program.cs:35` — 구 `DriveSplit.Expand`(문자열) 사용. 신형
  `Expand`(`RootChunk`)로 갱신: `indexer.Index(chunk.EnumRoot, folder, chunk.Recursive, …)`.
- `Blink.App/App.xaml.cs` — 변경 불필요(여전히 등록 폴더를 `ReindexAsync`에 넘김). 확장은
  `IndexingService` 내부에서 일어난다. Windows 전용 → 코드 리뷰로만 검증.

## 5. 호환성 분석 (기존 DB → 신버전)

| 항목 | 결과 |
|---|---|
| 스키마 | `documents` 구조·`schema_meta.version` 불변, 분할 비영속 → **마이그레이션 불필요** |
| 증분 스킵 | 청크가 `IterDocsUnder(chunkRoot)` 프리픽스 쿼리로 구 doc(절대경로 키)를 찾음 → 업그레이드 후 첫 실행도 **전체 재추출 아님**, 기존처럼 증분 |
| 가지치기 | 존재 기반 삭제(`!File.Exists`)·청크별 프리픽스 → 청크 합집합 = 구 단일루트 prune과 동일 집합, 오삭제 없음. `RootUnavailableException` 가드가 청크별 적용 → 더 안전 |
| 번들 마커 | 폴더 직속 파일은 정확히 한 청크가 열거 → `(Dir,Ext)` 카운트 동일 재계산, 중복/고아 마커 없음 |
| config.json | `folders[]` 재작성 안 함 → 구버전 **롤백도** 동일 DB·설정으로 정상 증분(전·후방 호환) |

## 6. 테스트 전략

전부 `Blink.Core` 로직 → macOS에서 `dotnet test`로 검증. App 와이어링은 코드 리뷰.

### A. RootExpander 트리 모양별 (임시 디렉토리 트리)

| 케이스 | 기대 |
|---|---|
| 얇은 체인(단일 서브·파일0, 5단계) | 루트 근처 `recursive=true` 청크 1개 |
| NAS형(얇다가 바닥에서 넓어짐) | passage 터널링 후 넓은 레벨에서 다수 청크 |
| 넓은-얕은(다수 서브) | 분기 깊이 cap 내 청크들, 과분할 없음 |
| passage에 흩어진 파일 존재 | 그 폴더 `recursive=false` 청크 + 하위 재귀 |
| leaf(서브0) | `recursive=true` 한 청크 |
| MaxDepth 초과 깊이 | depth cap에서 `recursive=true`로 꼬리 흡수 |
| AbsMaxDepth 초과(65단계 체인) | 가드에서 정지 |
| 없는/접근불가 루트 | 빈 리스트 |

### B. ★ 파티션 속성 테스트 (최고 가치)

생성한 트리에 대해, 모든 청크가 (recursive 플래그대로) 열거한 파일 집합이 **트리 전체 파일
집합과 정확히 일치(중복 0, 누락 0)** 함을 단언. 트리 모양 무관.

### C. Indexer 신 시그니처

- `recursive=false` → 직속 파일만 인덱싱(하위 미포함).
- `excludeRoot` 고정: enumRoot가 더 깊어도 excludeRoot의 `.blinkignore`·루트상대 패턴 적용.
- 구 오버로드 `Index(root, store, progress, ct)` 동작 보존.
- 비재귀 부모 청크 + 재귀 자식 청크 → 겹침 없이 전부 커버.

### D. ★ 기존 DB 업그레이드 회귀 테스트 (호환성 핵심)

1. 트리를 구 경로(단일 재귀 루트)로 store에 인덱싱.
2. 같은 트리를 신 청크 경로로 같은 store에 재인덱싱.
3. 단언: 중복 doc 0, doc 수 안정, mtime 미변경 파일 스킵, **최종 doc 집합(번들 마커 포함)이
   신규 단일루트 인덱싱 결과와 동일**.

### E. IndexingService 루프

Expand가 루프 안쪽에서 발생, `FolderCompleted`가 등록 폴더당 1회(청크당 아님) 발화.

### F. 가지치기 + 청크

- 파일 삭제 후 청크 인덱싱+prune → stale doc 제거.
- 청크 루트 소실 시 `RootUnavailableException`이 형제 청크를 안 지움.

가장 비중 둘 곳: **B(파티션 속성)**, **D(DB 업그레이드 회귀)**.

## Out of scope (YAGNI)

- `K`/`MaxDepth`/`AbsMaxDepth`의 설정 UI 노출 — 내부 const 유지.
- 워커 경로(`WorkerIndexer`) 청킹 적용 — 워커 연결 시 함께.
- 제외 규칙 변경의 소급 가지치기(기존에도 존재-기반이라 미적용; 본 변경의 회귀 아님).
- 단일 디렉토리 내 직속 파일이 수백만인 경우의 추가 분할 — 본래 분할 불가, 번들러가
  filename-only를 흡수하는 현 동작에 위임.
