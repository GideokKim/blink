namespace Blink.Core.Launch;

/// <summary>훅 글루가 수행할 액션. Forward = CallNextHookEx로 통과.</summary>
public enum HotkeyAction
{
    /// <summary>이벤트를 다음 훅/앱으로 통과시킨다.</summary>
    Forward,
    /// <summary>이벤트를 차단한다 (훅에서 1 반환).</summary>
    Swallow,
    /// <summary>이벤트를 차단하고, 핫키 발동 + 더미 키 주입을 수행한다.</summary>
    SwallowAndFire,
}

/// <summary>
/// 좌Alt+Space 전역 핫키의 판정 상태 머신.
/// 전용 펌프 스레드에서만 호출된다는 전제 — 스레드 안전성 없음(불필요).
/// </summary>
public sealed class HotkeyInterceptor
{
    /// <summary>주입 이벤트의 dwExtraInfo 마커 ("BLNK"). 글루가 SendInput 시 태깅.</summary>
    public const uint InjectionMarker = 0x424C4E4B;

    private const uint VkLeftAlt = 0xA4; // VK_LMENU — 우Alt(0xA5)는 의도적으로 미추적
    private const uint VkSpace   = 0x20;

    private bool _leftAltDown;
    private bool _swallowSpaceUp; // down을 삼켰으면 짝이 되는 up도 삼킨다

    public HotkeyAction Process(uint vk, bool isDown, bool isUp, ulong extraInfo)
    {
        // 우리가 주입한 더미 키: 상태에 반영하지 않고 통과.
        // (포그라운드 앱이 봐야 'Alt 단독' 메뉴 활성화가 깨진다.)
        if (extraInfo == InjectionMarker)
            return HotkeyAction.Forward;

        if (vk == VkLeftAlt)
        {
            _leftAltDown = isDown;
            return HotkeyAction.Forward; // Alt 자체는 항상 통과 (다른 Alt 조합 보존)
        }

        if (vk == VkSpace)
        {
            if (isDown && _leftAltDown)
            {
                if (_swallowSpaceUp)
                    return HotkeyAction.Swallow; // auto-repeat: 차단만, 재발동 없음
                _swallowSpaceUp = true;
                return HotkeyAction.SwallowAndFire;
            }
            if (isUp && _swallowSpaceUp)
            {
                _swallowSpaceUp = false;
                return HotkeyAction.Swallow; // down을 삼켰으면 up도 삼킨다
            }
        }

        return HotkeyAction.Forward;
    }
}
