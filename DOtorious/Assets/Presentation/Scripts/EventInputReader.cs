using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace JYW.Game.EventPlay
{
    internal static class EventInputReader
    {
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
        private static readonly Type KeyboardType = Type.GetType("UnityEngine.InputSystem.Keyboard, Unity.InputSystem");
        private static readonly PropertyInfo CurrentKeyboardProperty = KeyboardType?.GetProperty("current", BindingFlags.Public | BindingFlags.Static);
        private static readonly Dictionary<KeyCode, PropertyInfo> KeyProperties = new Dictionary<KeyCode, PropertyInfo>();
        private static readonly PropertyInfo AnyKeyProperty = KeyboardType?.GetProperty("anyKey", BindingFlags.Public | BindingFlags.Instance);

        private static readonly Type MouseType = Type.GetType("UnityEngine.InputSystem.Mouse, Unity.InputSystem");
        private static readonly PropertyInfo CurrentMouseProperty = MouseType?.GetProperty("current", BindingFlags.Public | BindingFlags.Static);
        private static readonly PropertyInfo[] MouseButtonProperties =
        {
            MouseType?.GetProperty("leftButton", BindingFlags.Public | BindingFlags.Instance),
            MouseType?.GetProperty("rightButton", BindingFlags.Public | BindingFlags.Instance),
            MouseType?.GetProperty("middleButton", BindingFlags.Public | BindingFlags.Instance),
            MouseType?.GetProperty("backButton", BindingFlags.Public | BindingFlags.Instance),
            MouseType?.GetProperty("forwardButton", BindingFlags.Public | BindingFlags.Instance)
        };
#endif

        public static bool TryIsPressed(KeyCode keyCode, out bool pressed)
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            pressed = Input.GetKey(keyCode); return true;
#elif ENABLE_INPUT_SYSTEM
            return TryReadInputSystemKey(keyCode, "isPressed", out pressed);
#else
            pressed = false; return false;
#endif
        }

        public static bool TryWasPressedThisFrame(KeyCode keyCode, out bool pressed)
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            pressed = Input.GetKeyDown(keyCode); return true;
#elif ENABLE_INPUT_SYSTEM
            return TryReadInputSystemKey(keyCode, "wasPressedThisFrame", out pressed);
#else
            pressed = false; return false;
#endif
        }

        public static bool IsSupported(KeyCode keyCode)
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            return keyCode != KeyCode.None;
#elif ENABLE_INPUT_SYSTEM
            if (KeyboardType == null || keyCode == KeyCode.None) return false;
            string propertyName = GetInputSystemPropertyName(keyCode);
            return !string.IsNullOrEmpty(propertyName) &&
                   KeyboardType.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance) != null;
#else
            return false;
#endif
        }

        public static bool TryWasAnyKeyboardOrMousePressedThisFrame(out bool pressed)
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            pressed = Input.anyKeyDown;
            return true;
#elif ENABLE_INPUT_SYSTEM
            pressed = false;
            bool supported = false;

            object keyboard = CurrentKeyboardProperty?.GetValue(null);
            object anyKey = keyboard != null ? AnyKeyProperty?.GetValue(keyboard) : null;
            if (TryReadInputSystemButton(anyKey, out bool keyboardPressed))
            {
                supported = true;
                if (keyboardPressed)
                {
                    pressed = true;
                    return true;
                }
            }

            object mouse = CurrentMouseProperty?.GetValue(null);
            if (mouse != null)
            {
                for (int i = 0; i < MouseButtonProperties.Length; i++)
                {
                    PropertyInfo buttonProperty = MouseButtonProperties[i];
                    object button = buttonProperty?.GetValue(mouse);
                    if (!TryReadInputSystemButton(button, out bool mousePressed)) continue;

                    supported = true;
                    if (mousePressed)
                    {
                        pressed = true;
                        return true;
                    }
                }
            }

            return supported;
#else
            pressed = false;
            return false;
#endif
        }

#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
        private static bool TryReadInputSystemButton(object buttonControl, out bool pressed)
        {
            pressed = false;
            if (buttonControl == null) return false;

            PropertyInfo pressedThisFrameProperty = buttonControl.GetType().GetProperty(
                "wasPressedThisFrame",
                BindingFlags.Public | BindingFlags.Instance);
            if (pressedThisFrameProperty?.PropertyType != typeof(bool)) return false;

            pressed = (bool)pressedThisFrameProperty.GetValue(buttonControl);
            return true;
        }

        private static bool TryReadInputSystemKey(KeyCode keyCode, string statePropertyName, out bool value)
        {
            value = false;
            object keyboard = CurrentKeyboardProperty?.GetValue(null);
            if (keyboard == null || KeyboardType == null) return false;
            if (!KeyProperties.TryGetValue(keyCode, out PropertyInfo keyProperty))
            {
                string propertyName = GetInputSystemPropertyName(keyCode);
                keyProperty = string.IsNullOrEmpty(propertyName) ? null : KeyboardType.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
                KeyProperties[keyCode] = keyProperty;
            }
            object keyControl = keyProperty?.GetValue(keyboard);
            PropertyInfo stateProperty = keyControl?.GetType().GetProperty(statePropertyName, BindingFlags.Public | BindingFlags.Instance);
            if (stateProperty?.PropertyType != typeof(bool)) return false;
            value = (bool)stateProperty.GetValue(keyControl); return true;
        }

        private static string GetInputSystemPropertyName(KeyCode keyCode)
        {
            string name = keyCode.ToString();
            if (name.Length == 1 && char.IsLetter(name[0])) return char.ToLowerInvariant(name[0]) + "Key";
            if (name.StartsWith("Alpha", StringComparison.Ordinal) && name.Length == 6) return "digit" + name[5] + "Key";
            if (name.StartsWith("Keypad", StringComparison.Ordinal) && name.Length == 7 && char.IsDigit(name[6])) return "numpad" + name[6] + "Key";
            if (name.StartsWith("F", StringComparison.Ordinal) && int.TryParse(name.Substring(1), out _)) return name.ToLowerInvariant() + "Key";
            switch (keyCode)
            {
                case KeyCode.Space: return "spaceKey";
                case KeyCode.Return: return "enterKey";
                case KeyCode.KeypadEnter: return "numpadEnterKey";
                case KeyCode.Escape: return "escapeKey";
                case KeyCode.Tab: return "tabKey";
                case KeyCode.Backspace: return "backspaceKey";
                case KeyCode.Delete: return "deleteKey";
                case KeyCode.Insert: return "insertKey";
                case KeyCode.Home: return "homeKey";
                case KeyCode.End: return "endKey";
                case KeyCode.PageUp: return "pageUpKey";
                case KeyCode.PageDown: return "pageDownKey";
                case KeyCode.UpArrow: return "upArrowKey";
                case KeyCode.DownArrow: return "downArrowKey";
                case KeyCode.LeftArrow: return "leftArrowKey";
                case KeyCode.RightArrow: return "rightArrowKey";
                case KeyCode.LeftShift: return "leftShiftKey";
                case KeyCode.RightShift: return "rightShiftKey";
                case KeyCode.LeftControl: return "leftCtrlKey";
                case KeyCode.RightControl: return "rightCtrlKey";
                case KeyCode.LeftAlt: return "leftAltKey";
                case KeyCode.RightAlt: return "rightAltKey";
                case KeyCode.CapsLock: return "capsLockKey";
                case KeyCode.Numlock: return "numLockKey";
                case KeyCode.ScrollLock: return "scrollLockKey";
                case KeyCode.Print: return "printScreenKey";
                case KeyCode.Pause: return "pauseKey";
                case KeyCode.BackQuote: return "backquoteKey";
                case KeyCode.Minus: return "minusKey";
                case KeyCode.Equals: return "equalsKey";
                case KeyCode.LeftBracket: return "leftBracketKey";
                case KeyCode.RightBracket: return "rightBracketKey";
                case KeyCode.Backslash: return "backslashKey";
                case KeyCode.Semicolon: return "semicolonKey";
                case KeyCode.Quote: return "quoteKey";
                case KeyCode.Comma: return "commaKey";
                case KeyCode.Period: return "periodKey";
                case KeyCode.Slash: return "slashKey";
                case KeyCode.KeypadPeriod: return "numpadPeriodKey";
                case KeyCode.KeypadDivide: return "numpadDivideKey";
                case KeyCode.KeypadMultiply: return "numpadMultiplyKey";
                case KeyCode.KeypadMinus: return "numpadMinusKey";
                case KeyCode.KeypadPlus: return "numpadPlusKey";
                case KeyCode.KeypadEquals: return "numpadEqualsKey";
                default: return null;
            }
        }
#endif
    }
}
