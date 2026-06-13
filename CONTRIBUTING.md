# 기여 가이드 (Contributing)

Blink에 관심 가져주셔서 감사합니다! 🙌

## 라이선스 (중요)

Blink는 **GNU General Public License v3.0 (GPL-3.0)** 오픈소스입니다. Pull Request를
제출하면, 기여물이 동일하게 **GPL-3.0으로 배포**되는 데 동의하는 것으로 간주합니다
(inbound = outbound). 별도의 저작권 양도(CLA)는 없으며 기여물의 저작권은 기여자에게
남습니다. 본인이 작성했거나 GPL-3.0으로 제공할 권리가 있는 코드만 제출해 주세요.

## 시작하기 전에

- 아키텍처나 동작이 바뀌는 **큰 변경은 먼저 이슈로 논의**해 주세요. 오타·작은 수정은
  바로 PR도 환영합니다.
- 버그·기능 제안은 이슈 템플릿을 이용해 주세요.

## 개발 환경

- **.NET 8 SDK**가 필요합니다.
- 검색 **엔진·CLI·인덱서 워커**(`Blink.Core`, `Blink.Cli`, `Blink.Indexer.Worker`)는
  크로스플랫폼이라 macOS·Linux·Windows 어디서나 빌드·테스트됩니다:

  ```bash
  dotnet test Blink.Core.Tests -c Release
  ```

- **WPF 앱**(`Blink.App`)은 `net8.0-windows` 대상이라 **Windows에서만** 빌드됩니다 —
  [BUILD-WINDOWS.md](BUILD-WINDOWS.md)를 참고하세요.

## 코딩 규칙

- 주변 코드의 스타일·네이밍·주석 밀도에 맞춰 작성해 주세요.
- 커밋 메시지는 `feat(app): …`, `fix(core): …` 같은 Conventional Commits 스타일을
  권장합니다(한국어 메시지도 좋습니다).

## PR 체크리스트

- [ ] 엔진·CLI 변경 시 테스트 통과
- [ ] 앱 변경 시 Windows에서 빌드·동작 확인
- [ ] 필요한 문서 업데이트
- [ ] 변경 내용을 설명하는 PR 본문

## 행동 강령

모든 참여자는 [행동 강령](CODE_OF_CONDUCT.md)을 따릅니다.

## 릴리스 (메인테이너용)

태그 기반 릴리스 절차는 [RELEASING.md](RELEASING.md)를 참고하세요.
