# README 데모 GIF 시나리오

README 최상단에 넣을 **히어로 GIF**(5~8초 루프)의 녹화·제작 가이드입니다.
한 장면으로 Blink의 핵심 차별점 — *"파일 이름을 몰라도 본문 한 조각이면 즉시 찾는다"* — 를
증명하는 것이 목표입니다.

> 앱은 Windows 전용이라 녹화는 Windows에서 직접 진행합니다. 이 문서는 그대로 따라
> 하면 되는 체크리스트입니다.

## 1. 스토리보드 (5~8초 루프)

| 시간 | 화면 | 포인트 |
|---|---|---|
| 0.0–0.5s | 깔끔한 바탕화면(작업표시줄만) | "검색 전" 평상 상태 |
| 0.5–1.0s | **왼쪽 Alt + Space** → 스포트라이트 패널이 화면 중앙에 떠오름 | 시그니처 등장. 가능하면 `Alt + Space` 키 오버레이 표시 |
| 1.0–2.5s | `3분기 매출` 을 한 글자씩 입력 → 결과가 **실시간**으로 좁혀짐 | 라이브 검색 = 속도감 |
| 2.5–4.5s | 결과 3~4개. 1위 `회의록_2026-03-12.docx` — 파일명엔 "3분기/매출"이 없는데 **매치된 문장 미리보기**에 검색어가 하이라이트됨. `.hwpx` 결과도 한 줄 보임 | ⭐핵심: 본문 검색 + 한컴 지원 |
| 4.5–5.5s | ↓ 방향키로 결과를 훑으며 미리보기 문장에서 잠깐 정지(읽을 시간 확보) | 미리보기 가치 전달 |
| 5.5–6.5s | **Enter** → 파일이 열리는 순간을 짧게 컷 → 패널 닫힘 | 보상(payoff) |
| 6.5–7.0s | 짧은 정지 후 처음으로 루프 | 매끄러운 반복 |

엔딩 대안: Word 실행이 느리거나 지저분하면 Enter 직후 바로 컷하거나, `Shift+Enter`로
탐색기에서 위치만 드러내고 끝내도 됩니다.

## 2. 연출용 데모 폴더

검색어 `3분기 매출` 이 **파일명이 아니라 본문**에만 들어가도록 구성합니다. 폴더 경로는
`C:\BlinkDemo\` 또는 NAS 느낌을 주려면 매핑 드라이브(예: `L:\공유문서\`)를 씁니다.
설정에서 이 폴더를 인덱싱 대상으로 추가한 뒤 녹화합니다.

| 파일 | 본문에 넣을 문장(미리보기에 노출) | 의도 |
|---|---|---|
| `회의록_2026-03-12.docx` | 이번 3분기 매출은 전 분기 대비 18% 증가했습니다. | ⭐히어로 매치(파일명 무관) |
| `보고서_초안.hwpx` | 3분기 매출 추이와 지역별 편차를 분석한다. | 한컴 hwpx 본문 검색 |
| `킥오프.pptx` | 신제품 출시 일정과 3분기 매출 목표를 공유합니다. | 다중 매치·포맷 다양성 |
| `예산안.xlsx` | 3분기 매출 예측치를 기준으로 편성한다. | Office 전반 |
| `점심메뉴.txt` | (검색어 없음 — 아무 내용) | 잡음 제외 대비 |
| `IMG_4821.jpg` | (이미지, 매치 없음) | 정확도 대비 |

준비 방법:
- `.docx` · `.pptx` · `.xlsx` 는 정식 OOXML 구조가 필요하므로 **MS Office 또는
  LibreOffice/구글독스**(무료)로 만들어 위 문장을 본문에 붙여 넣습니다(각 1줄, 2분).
- **`.hwpx` 는 한컴오피스가 없어도 됩니다.** Blink의 `HwpxParser` 는 zip 속
  `Contents/*.xml` 의 텍스트 노드만 읽으므로, 아래 PowerShell 스니펫으로 검색어가 든
  최소 hwpx 를 만들면 정상 인덱싱됩니다(데모에선 결과·미리보기로만 보이고 열지 않음).
- `.txt` 는 메모장으로 아무 내용. `.jpg` 는 아무 이미지 한 장.
- 파일명에 "3분기"나 "매출"이 **들어가지 않도록** 주의(본문 검색임을 증명하는 핵심).

### 폴더·텍스트 파일 자동 생성 (PowerShell)

폴더 구조와 `.txt`·`.jpg` 자리표시자는 아래 스니펫으로 한 번에 만들 수 있습니다.
Office/hwpx 4개 문서만 직접 만들어 같은 폴더에 넣으면 됩니다.

```powershell
# 데모 폴더 생성 (NAS 느낌을 주려면 $root 를 'L:\공유문서' 등으로 변경)
$root = 'C:\BlinkDemo'
New-Item -ItemType Directory -Force -Path $root, "$root\사진모음" | Out-Null

# 검색어가 없는 잡음용 .txt (제외/정확도 대비용)
@'
오늘 점심은 김치찌개와 제육볶음.
내일은 회식 장소를 정해야 한다.
'@ | Set-Content -Path "$root\점심메뉴.txt" -Encoding utf8

# 이미지 자리표시자 (실제 사진으로 교체해도 됨)
$png = [Convert]::FromBase64String('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==')
[IO.File]::WriteAllBytes("$root\IMG_4821.jpg", $png)
[IO.File]::WriteAllBytes("$root\사진모음\IMG_4822.jpg", $png)

# 검증: 파일명에 "3분기"나 "매출"이 들어간 게 없어야 함(본문 검색 증명)
Get-ChildItem $root -Recurse -File | Where-Object { $_.Name -match '3분기|매출' } |
  ForEach-Object { Write-Warning "파일명에 검색어 포함됨: $($_.Name)" }

# --- .hwpx 생성 (한컴오피스 불필요) ---
# HwpxParser 는 zip 속 Contents/*.xml 의 텍스트 노드만 읽으므로, 검색어가 든 문장을
# 담은 Contents/section0.xml 하나면 충분하다.
Add-Type -AssemblyName System.IO.Compression.FileSystem
$hwpx = "$root\보고서_초안.hwpx"
$sentence = '3분기 매출 추이와 지역별 편차를 분석한다.'
if (Test-Path $hwpx) { Remove-Item $hwpx }
$zip = [System.IO.Compression.ZipFile]::Open($hwpx, 'Create')
try {
  $entry  = $zip.CreateEntry('Contents/section0.xml')
  $writer = New-Object System.IO.StreamWriter($entry.Open(), (New-Object System.Text.UTF8Encoding($false)))
  $writer.Write("<?xml version=`"1.0`" encoding=`"UTF-8`"?><sec><p><t>$sentence</t></p></sec>")
  $writer.Dispose()
} finally { $zip.Dispose() }

Write-Host "데모 폴더 준비 완료: $root  (.docx/.pptx/.xlsx 3개만 수동 추가)"
```

> 직접 만드는 Office 3개 문서(`.docx`/`.pptx`/`.xlsx`)에 넣을 문장은 위 표를 그대로
> 복사해 붙여 넣으세요. `.hwpx`·`.txt`·`.jpg` 는 위 스니펫이 자동 생성합니다.

## 3. 녹화·제작 사양

- **툴**: [ScreenToGif](https://www.screentogif.com/) — 무료, Windows, 영역 녹화 → GIF
  최적화·프레임 편집까지 한 번에. (대안: shareX)
- **캡처 영역**: 스포트라이트 패널 위주. 너무 넓게 잡지 말 것.
- **표시폭/프레임레이트**: README 기준 폭 **~760px**, **15–18fps**.
- **길이**: 5–8초, 루프.
- **용량 목표**: GitHub 로딩 속도를 위해 **3MB 이하**. ScreenToGif 최적화 또는
  [gifski](https://gif.ski/) 로 압축. 더 작게 원하면 `.mp4` + `<video>` 태그 대안 가능
  (단, README에서 자동재생·루프는 GIF가 가장 무난).
- **연출 팁**:
  - 깨끗한 단색/단순 배경, 트레이·메신저 알림 끄기.
  - Blink 기본 테마/액센트 사용.
  - 타이핑은 또박또박하되 너무 느리지 않게(실시간 좁혀짐이 보여야 함).
  - 시작·끝의 죽은 프레임을 트림해 루프를 매끄럽게.

## 4. README 반영

녹화·최적화된 파일을 `docs/assets/blink-search.gif` 로 저장한 뒤, README의
`<!-- TODO: 스포트라이트 검색 GIF/스크린샷 -->` 자리에 아래를 넣습니다.

```md
<p align="center">
  <img src="docs/assets/blink-search.gif" alt="Blink 본문 검색 데모 — 왼쪽 Alt+Space로 띄워 내용 한 조각으로 문서를 찾는 모습" width="760">
</p>
<p align="center"><sub>왼쪽 Alt + Space → 내용 한 조각만 입력하면 문서가 바로 떠오릅니다.</sub></p>
```

`README.en.md` 의 동일 위치에도 영문 캡션으로 반영합니다.

```md
<p align="center">
  <img src="docs/assets/blink-search.gif" alt="Blink full-text search demo — summon with Left Alt+Space and find a document by a fragment of its content" width="760">
</p>
<p align="center"><sub>Left Alt + Space → type a fragment of the content and the document surfaces instantly.</sub></p>
```

## 5. 체크리스트

- [ ] PowerShell 스니펫 실행(폴더 + `.hwpx`/`.txt`/`.jpg` 자동 생성)
- [ ] `.docx`/`.pptx`/`.xlsx` 3개를 표의 문장으로 만들어 데모 폴더에 추가(파일명에 검색어 미포함)
- [ ] 설정에서 데모 폴더 인덱싱 추가 + 인덱싱 완료 대기
- [ ] 배경/알림 정리 후 ScreenToGif로 스토리보드대로 녹화
- [ ] 5–8초로 트림, 시작/끝 프레임 정리, 3MB 이하로 최적화
- [ ] `docs/assets/blink-search.gif` 저장
- [ ] `README.md` · `README.en.md` 의 TODO 자리에 스니펫 삽입
- [ ] GitHub에서 GIF가 정상 렌더·재생되는지 확인

## 6. 현재 데모(v1) 개선 사항

첫 녹화본(`docs/assets/blink-search.gif`, 1463×827 · 약 13.6초)을 검토한 결과입니다.
다음 재녹화 때 우선순위 순으로 반영합니다.

1. **바탕화면 교체 (최우선)** — 현재 배경이 물범 사진이라 시선이 분산되고, 모래색이
   패널 그림자·본문 글자와 대비가 약합니다. **단색 다크 배경(`#0B0D12`)** 또는 잔잔한
   다크 그라데이션으로 바꾸면 스포트라이트 패널이 깔끔하게 떠 보입니다.
2. **엔딩을 "본문 매치" 문서로** — 현재 마지막에 여는 파일이 `점심메뉴.txt`(검색어가
   없는 잡음 파일)라 "본문으로 찾았다"는 메시지가 약해집니다. **여는 파일을
   `회의록_2026-03-12.docx` 처럼 본문에만 검색어가 있는 문서로** 바꿔, 열린 문서에서
   "3분기 매출" 문장이 보이게 하면 핵심 차별점이 끝까지 각인됩니다.
3. **길이 단축 (13.6초 → 7~9초)** — 시작의 빈 배경(약 1초)과 빈 에디터 창이 떠 있는
   구간을 트림하고, 검색을 1회 흐름으로 압축. 히어로 루프는 짧을수록 잘 읽힙니다.
4. **빈 창 데드타임 제거** — 파일을 열 때 잠깐 나오는 빈 에디터 창 프레임을 잘라
   바로 내용이 보이도록.
5. **패널 중심 크롭 (가독성)** — 전체 화면(1463×827)을 760px로 줄이면 글자가 작습니다.
   패널과 결과 영역 위주로 크롭하면 검색어·미리보기 문장이 선명해집니다.
6. **매치 하이라이트 강조** — 강조색을 약간 밝게(아래 7번) 하고, 미리보기에서 매치된
   글자의 대비를 확보해 "어디가 맞았는지" 한눈에 보이게.
7. **키 입력 힌트(선택)** — `Alt + Space` 트리거가 화면에 안 보이므로, 작은 키 오버레이를
   넣거나 README 캡션(이미 추가됨)으로 보완.
8. **작업표시줄·트레이 정리** — 알림 풍선/불필요 아이콘이 잡히지 않도록(현재는 거의
   안 보여 양호).

## 7. 권장 테마·강조색

데모는 OS 설정에 따라 색이 달라지지 않도록 **값 고정(dark)** 으로 녹화합니다. 아래는
`%APPDATA%\Blink\config.json` 키 기준(또는 설정 창의 테마/강조색 피커에서 동일하게 지정).

| 항목 | 권장 값 | 비고 |
|---|---|---|
| `theme_mode` | `"value"` | OS 추종(`"system"`) 끄고 고정 |
| `base_color` | `#0B0D12` | 앱 기본 다크. 스포트라이트에 가장 잘 어울림 |
| `accent` | `#3B7FE3` | 앱 기본 블루(배지·후원 버튼과 톤 일치) |

- **강조색 대안(선택)**: 매치 하이라이트·캐럿·상태 점을 더 튀게 하고 싶으면
  `#4C8DFF`(조금 더 밝은 블루)를 권장. 초록(`#3FB950`)은 "성공" 느낌이라 검색 데모에는
  덜 어울립니다.
- **배경 매칭**: 바탕화면도 `#0B0D12` 단색으로 맞추면 패널이 배경과 자연스럽게 이어져
  가장 정돈돼 보입니다(개선 1번과 같은 색).
- 설정 위치: 트레이 → 설정 → 테마/강조색, 또는 `config.json` 직접 편집 후 앱 재시작.
