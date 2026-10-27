package com.padkey.app;

import android.view.KeyEvent;

/** Maps Android hardware-keyboard key codes to USB HID usage IDs. */
public final class AndroidKeyMap {
    private AndroidKeyMap() { }

    public static int toHid(int keyCode) {
        if (keyCode >= KeyEvent.KEYCODE_A && keyCode <= KeyEvent.KEYCODE_Z) {
            return 0x04 + (keyCode - KeyEvent.KEYCODE_A);
        }
        if (keyCode >= KeyEvent.KEYCODE_1 && keyCode <= KeyEvent.KEYCODE_9) {
            return 0x1E + (keyCode - KeyEvent.KEYCODE_1);
        }
        if (keyCode == KeyEvent.KEYCODE_0) return 0x27;
        if (keyCode >= KeyEvent.KEYCODE_F1 && keyCode <= KeyEvent.KEYCODE_F12) {
            return 0x3A + (keyCode - KeyEvent.KEYCODE_F1);
        }
        switch (keyCode) {
            case KeyEvent.KEYCODE_ESCAPE: return 0x29;
            case KeyEvent.KEYCODE_ENTER:
            case KeyEvent.KEYCODE_NUMPAD_ENTER: return 0x28;
            case KeyEvent.KEYCODE_DEL: return 0x2A;
            case KeyEvent.KEYCODE_FORWARD_DEL: return 0x4C;
            case KeyEvent.KEYCODE_TAB: return 0x2B;
            case KeyEvent.KEYCODE_SPACE: return 0x2C;
            case KeyEvent.KEYCODE_GRAVE: return 0x35;
            case KeyEvent.KEYCODE_MINUS: return 0x2D;
            case KeyEvent.KEYCODE_EQUALS: return 0x2E;
            case KeyEvent.KEYCODE_LEFT_BRACKET: return 0x2F;
            case KeyEvent.KEYCODE_RIGHT_BRACKET: return 0x30;
            case KeyEvent.KEYCODE_BACKSLASH: return 0x31;
            case KeyEvent.KEYCODE_SEMICOLON: return 0x33;
            case KeyEvent.KEYCODE_APOSTROPHE: return 0x34;
            case KeyEvent.KEYCODE_COMMA: return 0x36;
            case KeyEvent.KEYCODE_PERIOD: return 0x37;
            case KeyEvent.KEYCODE_SLASH: return 0x38;
            case KeyEvent.KEYCODE_CAPS_LOCK: return 0x39;
            case KeyEvent.KEYCODE_SYSRQ: return 0x46;
            case KeyEvent.KEYCODE_SCROLL_LOCK: return 0x47;
            case KeyEvent.KEYCODE_BREAK: return 0x48;
            case KeyEvent.KEYCODE_INSERT: return 0x49;
            case KeyEvent.KEYCODE_MOVE_HOME: return 0x4A;
            case KeyEvent.KEYCODE_PAGE_UP: return 0x4B;
            case KeyEvent.KEYCODE_MOVE_END: return 0x4D;
            case KeyEvent.KEYCODE_PAGE_DOWN: return 0x4E;
            case KeyEvent.KEYCODE_DPAD_RIGHT: return 0x4F;
            case KeyEvent.KEYCODE_DPAD_LEFT: return 0x50;
            case KeyEvent.KEYCODE_DPAD_DOWN: return 0x51;
            case KeyEvent.KEYCODE_DPAD_UP: return 0x52;
            case KeyEvent.KEYCODE_NUM_LOCK: return 0x53;
            case KeyEvent.KEYCODE_MUTE: return 0x7F;
            case KeyEvent.KEYCODE_VOLUME_UP: return 0x80;
            case KeyEvent.KEYCODE_VOLUME_DOWN: return 0x81;
            case KeyEvent.KEYCODE_MEDIA_PLAY_PAUSE: return 0xCD;
            case KeyEvent.KEYCODE_MEDIA_PREVIOUS: return 0xB6;
            case KeyEvent.KEYCODE_MEDIA_NEXT: return 0xB5;
            case KeyEvent.KEYCODE_MENU: return 0x65;
            case KeyEvent.KEYCODE_SHIFT_LEFT: return 0xE1;
            case KeyEvent.KEYCODE_SHIFT_RIGHT: return 0xE5;
            case KeyEvent.KEYCODE_CTRL_LEFT: return 0xE0;
            case KeyEvent.KEYCODE_CTRL_RIGHT: return 0xE4;
            case KeyEvent.KEYCODE_ALT_LEFT: return 0xE2;
            case KeyEvent.KEYCODE_ALT_RIGHT: return 0xE6;
            case KeyEvent.KEYCODE_META_LEFT: return 0xE3;
            case KeyEvent.KEYCODE_META_RIGHT: return 0xE7;
            default: return 0;
        }
    }
}