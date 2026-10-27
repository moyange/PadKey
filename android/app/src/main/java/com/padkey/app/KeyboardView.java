package com.padkey.app;

import android.content.Context;
import android.graphics.Canvas;
import android.graphics.LinearGradient;
import android.graphics.Paint;
import android.graphics.RectF;
import android.graphics.Shader;
import android.graphics.Typeface;
import android.util.AttributeSet;
import android.view.HapticFeedbackConstants;
import android.view.MotionEvent;
import android.view.View;

import java.util.ArrayList;
import java.util.HashMap;
import java.util.List;
import java.util.Map;

/**
 * A 75% ANSI keyboard drawn with Canvas. Multi-touch is handled per pointer, so
 * chords such as Shift + A work naturally.
 */
public class KeyboardView extends View {
    private static final float WIDTH_UNITS = 16.25f;
    private static final int ROWS = 6;
    private static final float HEIGHT_UNITS = 6f;

    private enum Type { NORMAL, MOD, ACCENT, NAV, SPACE, FN }

    private static final class Key {
        final String label;
        final String sub;
        final int hid;
        final int fnHid;
        final float x, y, w, h;
        final Type type;
        final RectF rect = new RectF();
        boolean pressed;
        int pointerId = -1;

        Key(String label, String sub, int hid, int fnHid, float x, float y, float w, float h, Type type) {
            this.label = label;
            this.sub = sub;
            this.hid = hid;
            this.fnHid = fnHid;
            this.x = x;
            this.y = y;
            this.w = w;
            this.h = h;
            this.type = type;
        }
    }

    private final List<Key> keys = new ArrayList<>();
    private final Map<Integer, Key> pointerKeys = new HashMap<>();
    private final Map<Integer, Integer> pointerCodes = new HashMap<>();

    private final Paint fillPaint = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint strokePaint = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint textPaint = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint subPaint = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint boardPaint = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint boardStroke = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint highlightPaint = new Paint(Paint.ANTI_ALIAS_FLAG);

    private final float density;
    private float ux, uy, gap, originX, originY;
    private boolean fnHeld;
    private int fnPointer = -1;
    private boolean haptics = true;
    private ConnectionManager connection;

    public KeyboardView(Context context) {
        this(context, null);
    }

    public KeyboardView(Context context, AttributeSet attrs) {
        super(context, attrs);
        density = getResources().getDisplayMetrics().density;
        setFocusable(true);
        setClickable(true);
        buildKeys();
        setupPaints();
    }

    public void setConnection(ConnectionManager connection) {
        this.connection = connection;
    }

    public void setHaptics(boolean enabled) {
        this.haptics = enabled;
    }

    private float dp(float v) { return v * density; }

    private void setupPaints() {
        fillPaint.setStyle(Paint.Style.FILL);
        strokePaint.setStyle(Paint.Style.STROKE);
        strokePaint.setStrokeWidth(Math.max(1f, dp(0.8f)));
        highlightPaint.setStyle(Paint.Style.STROKE);
        highlightPaint.setStrokeWidth(dp(1.2f));
        textPaint.setTypeface(Typeface.create("sans-serif-medium", Typeface.NORMAL));
        textPaint.setTextAlign(Paint.Align.CENTER);
        subPaint.setTypeface(Typeface.create("sans-serif", Typeface.NORMAL));
        subPaint.setTextAlign(Paint.Align.LEFT);
        boardPaint.setStyle(Paint.Style.FILL);
        boardStroke.setStyle(Paint.Style.STROKE);
        boardStroke.setStrokeWidth(dp(1));
    }

    private void buildKeys() {
        keys.clear();

        // Row 0 — function row
        add(0, "Esc", null, 0x29, 0, 0f, 1f, Type.ACCENT);
        String[] media = {"MUTE", "VOL-", "VOL+", "PLAY", "PREV", "NEXT"};
        int[] mediaHid = {0x7F, 0x81, 0x80, 0xCD, 0xB6, 0xB5};
        for (int i = 0; i < 12; i++) {
            float x = i < 4 ? 1.25f + i : i < 8 ? 5.5f + (i - 4) : 9.75f + (i - 8);
            String sub = i < 6 ? media[i] : null;
            int fn = i < 6 ? mediaHid[i] : 0;
            add(0, "F" + (i + 1), sub, 0x3A + i, fn, x, 1f, Type.NORMAL);
        }
        add(0, "PrtSc", null, 0x46, 0, 14.0f, 1f, Type.NAV);
        add(0, "Del", null, 0x4C, 0, 15.25f, 1f, Type.NAV);

        // Row 1 — numbers
        String[] numLabels = {"\u0060", "1", "2", "3", "4", "5", "6", "7", "8", "9", "0", "-", "="};
        String[] numSubs = {"~", "!", "@", "#", "$", "%", "^", "&", "*", "(", ")", "_", "+"};
        int[] numHids = {0x35, 0x1E, 0x1F, 0x20, 0x21, 0x22, 0x23, 0x24, 0x25, 0x26, 0x27, 0x2D, 0x2E};
        for (int i = 0; i < numLabels.length; i++) {
            add(1, numLabels[i], numSubs[i], numHids[i], 0, i, 1f, Type.NORMAL);
        }
        add(1, "Backspace", null, 0x2A, 0, 13f, 2.25f, Type.MOD);
        add(1, "Home", null, 0x4A, 0, 15.25f, 1f, Type.NAV);

        // Row 2 — QWERTY
        add(2, "Tab", null, 0x2B, 0, 0f, 1.5f, Type.MOD);
        String[] qLabels = {"Q", "W", "E", "R", "T", "Y", "U", "I", "O", "P", "[", "]"};
        String[] qSubs = {null, null, null, null, null, null, null, null, null, null, "{", "}"};
        int[] qHids = {0x14, 0x1A, 0x08, 0x15, 0x17, 0x1C, 0x18, 0x0C, 0x12, 0x13, 0x2F, 0x30};
        for (int i = 0; i < qLabels.length; i++) {
            add(2, qLabels[i], qSubs[i], qHids[i], 0, 1.5f + i, 1f, Type.NORMAL);
        }
        add(2, "\\", "|", 0x31, 0, 13.5f, 1.75f, Type.NORMAL);
        add(2, "PgUp", null, 0x4B, 0, 15.25f, 1f, Type.NAV);

        // Row 3 — home row
        add(3, "Caps", null, 0x39, 0, 0f, 1.75f, Type.MOD);
        String[] aLabels = {"A", "S", "D", "F", "G", "H", "J", "K", "L", ";", "'"};
        String[] aSubs = {null, null, null, null, null, null, null, null, null, ":", "\""};
        int[] aHids = {0x04, 0x16, 0x07, 0x09, 0x0A, 0x0B, 0x0D, 0x0E, 0x0F, 0x33, 0x34};
        for (int i = 0; i < aLabels.length; i++) {
            add(3, aLabels[i], aSubs[i], aHids[i], 0, 1.75f + i, 1f, Type.NORMAL);
        }
        add(3, "Enter", null, 0x28, 0, 12.75f, 2.5f, Type.ACCENT);
        add(3, "PgDn", null, 0x4E, 0, 15.25f, 1f, Type.NAV);

        // Row 4 — shift row
        add(4, "Shift", null, 0xE1, 0, 0f, 2.25f, Type.MOD);
        String[] zLabels = {"Z", "X", "C", "V", "B", "N", "M", ",", ".", "/"};
        String[] zSubs = {null, null, null, null, null, null, null, "<", ">", "?"};
        int[] zHids = {0x1D, 0x1B, 0x06, 0x19, 0x05, 0x11, 0x10, 0x36, 0x37, 0x38};
        for (int i = 0; i < zLabels.length; i++) {
            add(4, zLabels[i], zSubs[i], zHids[i], 0, 2.25f + i, 1f, Type.NORMAL);
        }
        add(4, "Shift", null, 0xE5, 0, 12.25f, 2f, Type.MOD);
        add(4, "\u2191", null, 0x52, 0, 14.25f, 1f, Type.NAV);
        add(4, "End", null, 0x4D, 0, 15.25f, 1f, Type.NAV);

        // Row 5 — bottom
        add(5, "Ctrl", null, 0xE0, 0, 0f, 1.25f, Type.MOD);
        add(5, "Win", null, 0xE3, 0, 1.25f, 1.25f, Type.MOD);
        add(5, "Alt", null, 0xE2, 0, 2.5f, 1.25f, Type.MOD);
        add(5, "", "space", 0x2C, 0, 3.75f, 5.75f, Type.SPACE);
        add(5, "Alt", null, 0xE6, 0, 9.5f, 1.25f, Type.MOD);
        add(5, "Fn", null, 0, 0, 10.75f, 1.25f, Type.FN);
        add(5, "Ctrl", null, 0xE4, 0, 12f, 1.25f, Type.MOD);
        add(5, "\u2190", null, 0x50, 0, 13.25f, 1f, Type.NAV);
        add(5, "\u2193", null, 0x51, 0, 14.25f, 1f, Type.NAV);
        add(5, "\u2192", null, 0x4F, 0, 15.25f, 1f, Type.NAV);
    }

    private void add(int row, String label, String sub, int hid, int fnHid, float x, float w, Type type) {
        keys.add(new Key(label, sub, hid, fnHid, x, row, w, 1f, type));
    }

    @Override
    protected void onSizeChanged(int w, int h, int oldw, int oldh) {
        super.onSizeChanged(w, h, oldw, oldh);
        float padX = dp(8);
        float padY = dp(8);
        gap = Math.min(dp(5), Math.max(dp(2.5f), (w - padX * 2) / WIDTH_UNITS * 0.05f));
        ux = (w - padX * 2) / WIDTH_UNITS;
        float availableH = h - padY * 2 - gap * (ROWS - 1);
        uy = Math.min(availableH / HEIGHT_UNITS, ux * 1.30f);
        originX = (w - WIDTH_UNITS * ux) / 2f;
        float boardH = HEIGHT_UNITS * uy + gap * (ROWS - 1);
        originY = (h - boardH) / 2f;

        for (Key k : keys) {
            k.rect.set(
                    originX + k.x * ux + gap / 2f,
                    originY + k.y * (uy + gap) + gap / 2f,
                    originX + (k.x + k.w) * ux - gap / 2f,
                    originY + k.y * (uy + gap) + uy - gap / 2f);
        }
        invalidate();
    }

    @Override
    protected void onDraw(Canvas canvas) {
        super.onDraw(canvas);
        if (ux <= 0 || uy <= 0) return;

        float boardTop = originY - dp(9);
        float boardBottom = originY + HEIGHT_UNITS * uy + gap * (ROWS - 1) + dp(9);
        RectF board = new RectF(originX - dp(9), boardTop,
                originX + WIDTH_UNITS * ux + dp(9), boardBottom);
        boardPaint.setShader(new LinearGradient(0, board.top, 0, board.bottom,
                0xFF0C1119, 0xFF080C12, Shader.TileMode.CLAMP));
        canvas.drawRoundRect(board, dp(16), dp(16), boardPaint);
        boardStroke.setColor(0xFF1C2836);
        canvas.drawRoundRect(board, dp(16), dp(16), boardStroke);

        for (Key k : keys) drawKey(canvas, k);
    }

    private void drawKey(Canvas canvas, Key k) {
        RectF r = k.rect;
        float radius = Math.min(dp(9), r.height() * 0.18f);
        boolean pressed = k.pressed || (k.type == Type.FN && fnHeld);

        int fill, stroke, text;
        switch (k.type) {
            case ACCENT:
                fill = pressed ? 0xFF63E3FF : 0xFF0D3540;
                stroke = pressed ? 0xFFC8F7FF : 0xFF1D6172;
                text = pressed ? 0xFF041016 : 0xFF8BEEFF;
                break;
            case MOD:
                fill = pressed ? 0xFF63E3FF : 0xFF141B26;
                stroke = pressed ? 0xFFC8F7FF : 0xFF232F40;
                text = pressed ? 0xFF041016 : 0xFFB4C1D4;
                break;
            case NAV:
                fill = pressed ? 0xFF63E3FF : 0xFF121A25;
                stroke = pressed ? 0xFFC8F7FF : 0xFF202B3A;
                text = pressed ? 0xFF041016 : 0xFFB9C7DA;
                break;
            case FN:
                fill = fnHeld ? 0xFF63E3FF : 0xFF101721;
                stroke = fnHeld ? 0xFFC8F7FF : 0xFF1C2634;
                text = fnHeld ? 0xFF041016 : 0xFFB9C7DA;
                break;
            case SPACE:
                fill = pressed ? 0xFF63E3FF : 0xFF18202C;
                stroke = pressed ? 0xFFC8F7FF : 0xFF283548;
                text = pressed ? 0xFF041016 : 0xFF738197;
                break;
            default:
                fill = pressed ? 0xFF63E3FF : 0xFF1A2230;
                stroke = pressed ? 0xFFC8F7FF : 0xFF2C3A4D;
                text = pressed ? 0xFF041016 : 0xFFE9F1FB;
                break;
        }

        fillPaint.setShader(null);
        fillPaint.setColor(fill);
        canvas.drawRoundRect(r, radius, radius, fillPaint);

        strokePaint.setColor(stroke);
        canvas.drawRoundRect(r, radius, radius, strokePaint);

        if (!pressed) {
            highlightPaint.setColor(0x14FFFFFF);
            canvas.drawLine(r.left + radius, r.top + dp(1), r.right - radius, r.top + dp(1), highlightPaint);
        }

        float cx = r.centerX();
        float cy = r.centerY();
        float mainSize = Math.min(r.height() * 0.30f, dp(15));
        if (k.label.length() >= 5) mainSize = Math.min(mainSize, r.width() * 0.16f);
        if (k.label.length() >= 8) mainSize = Math.min(mainSize, r.width() * 0.13f);
        mainSize = Math.max(mainSize, dp(8));
        textPaint.setTextSize(mainSize);
        textPaint.setColor(text);
        textPaint.setFakeBoldText(false);

        Paint.FontMetrics fm = textPaint.getFontMetrics();
        float baseline = cy - (fm.ascent + fm.descent) / 2f;
        if (k.sub != null && !k.label.isEmpty()) {
            baseline += mainSize * 0.18f;
            subPaint.setTextSize(Math.max(dp(6.5f), Math.min(dp(8), r.height() * 0.16f)));
            boolean mediaHighlight = fnHeld && k.fnHid != 0;
            subPaint.setColor(mediaHighlight ? 0xFF63E3FF : (pressed ? 0xCC041016 : 0xFF74839A));
            subPaint.setFakeBoldText(mediaHighlight);
            canvas.drawText(k.sub, r.left + dp(5), r.top + dp(10), subPaint);
            subPaint.setFakeBoldText(false);
        }
        if (!k.label.isEmpty()) {
            canvas.drawText(k.label, cx, baseline, textPaint);
        } else if (k.sub != null) {
            // Space bar: draw the tiny label centered.
            subPaint.setTextSize(dp(8));
            subPaint.setTextAlign(Paint.Align.CENTER);
            subPaint.setColor(text);
            canvas.drawText(k.sub, cx, cy + dp(3), subPaint);
            subPaint.setTextAlign(Paint.Align.LEFT);
        }

        if (fnHeld && k.fnHid != 0) {
            fillPaint.setColor(0xFF63E3FF);
            canvas.drawCircle(r.right - dp(6), r.top + dp(6), dp(1.5f), fillPaint);
        }
    }

    @Override
    public boolean onTouchEvent(MotionEvent event) {
        int action = event.getActionMasked();
        switch (action) {
            case MotionEvent.ACTION_DOWN:
                handleDown(event.getPointerId(0), event.getX(0), event.getY(0));
                break;
            case MotionEvent.ACTION_POINTER_DOWN: {
                int idx = event.getActionIndex();
                handleDown(event.getPointerId(idx), event.getX(idx), event.getY(idx));
                break;
            }
            case MotionEvent.ACTION_UP:
                handleUp(event.getPointerId(0));
                break;
            case MotionEvent.ACTION_POINTER_UP: {
                int idx = event.getActionIndex();
                handleUp(event.getPointerId(idx));
                break;
            }
            case MotionEvent.ACTION_CANCEL:
                releaseAll();
                break;
            default:
                break;
        }
        return true;
    }

    private void handleDown(int pointerId, float x, float y) {
        if (pointerKeys.containsKey(pointerId)) return;
        Key key = findKey(x, y);
        if (key == null) return;

        if (key.type == Type.FN) {
            fnHeld = true;
            fnPointer = pointerId;
            pointerKeys.put(pointerId, key);
            key.pointerId = pointerId;
            invalidate();
            haptic(HapticFeedbackConstants.KEYBOARD_TAP);
            return;
        }

        int code = (fnHeld && key.fnHid != 0) ? key.fnHid : key.hid;
        if (code == 0) return;

        pointerKeys.put(pointerId, key);
        pointerCodes.put(pointerId, code);
        key.pointerId = pointerId;
        key.pressed = true;
        if (connection != null) connection.sendKey(code, true);
        invalidate();
        haptic(HapticFeedbackConstants.KEYBOARD_TAP);
    }

    private void handleUp(int pointerId) {
        Key key = pointerKeys.remove(pointerId);
        if (key == null) return;
        if (key.type == Type.FN) {
            if (fnPointer == pointerId) {
                fnHeld = false;
                fnPointer = -1;
            }
            key.pointerId = -1;
            invalidate();
            return;
        }
        Integer code = pointerCodes.remove(pointerId);
        key.pressed = false;
        key.pointerId = -1;
        if (code != null && connection != null) connection.sendKey(code, false);
        invalidate();
    }

    private Key findKey(float x, float y) {
        for (Key k : keys) {
            if (k.rect.contains(x, y)) return k;
        }
        return null;
    }

    /** Release every held key. Safe to call from Activity.onPause(). */
    public void releaseAll() {
        for (Map.Entry<Integer, Integer> e : pointerCodes.entrySet()) {
            if (connection != null) connection.sendKey(e.getValue(), false);
        }
        pointerCodes.clear();
        for (Key k : pointerKeys.values()) {
            k.pressed = false;
            k.pointerId = -1;
        }
        pointerKeys.clear();
        fnHeld = false;
        fnPointer = -1;
        invalidate();
    }

    private void haptic(int constant) {
        if (haptics) performHapticFeedback(constant);
    }

    @Override
    protected void onDetachedFromWindow() {
        releaseAll();
        super.onDetachedFromWindow();
    }
}
