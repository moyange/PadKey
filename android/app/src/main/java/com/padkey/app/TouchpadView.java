package com.padkey.app;

import android.content.Context;
import android.graphics.Canvas;
import android.graphics.LinearGradient;
import android.graphics.Paint;
import android.graphics.RectF;
import android.graphics.Shader;
import android.graphics.Typeface;
import android.os.Handler;
import android.os.Looper;
import android.os.SystemClock;
import android.util.AttributeSet;
import android.util.SparseArray;
import android.view.HapticFeedbackConstants;
import android.view.MotionEvent;
import android.view.View;

import java.util.ArrayList;
import java.util.Iterator;
import java.util.List;

/**
 * Touchpad with the gestures people expect from a laptop:
 * one-finger move, tap = left click, two-finger scroll/tap = right click,
 * three-finger tap = middle click, double-tap-drag and long-press-drag.
 * Three large buttons along the bottom give explicit L/M/R clicks.
 */
public class TouchpadView extends View {
    private static final int MODE_IDLE = 0;
    private static final int MODE_ONE = 1;
    private static final int MODE_TWO = 2;
    private static final int MODE_THREE = 3;
    private static final int MODE_DRAG = 4;
    private static final int MODE_BUTTON = 5;

    private static final long TAP_TIMEOUT = 280;
    private static final long DOUBLE_TAP_TIMEOUT = 420;
    private static final long LONG_PRESS_TIMEOUT = 460;
    private static final float TAP_SLOP_DP = 11f;
    private static final float DOUBLE_TAP_SLOP_DP = 38f;
    private static final float SCROLL_STEP_DP = 18f;

    private static final String[] BUTTON_NAMES = {"left", "middle", "right"};
    private static final String[] BUTTON_LABELS = {"L", "M", "R"};
    private static final String[] BUTTON_SUBS = {"左键", "中键", "右键"};

    private final Paint panelPaint = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint borderPaint = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint sensorPaint = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint sensorBorder = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint dotPaint = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint textPaint = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint hintPaint = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint touchPaint = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint touchCorePaint = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint ripplePaint = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint chipPaint = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint buttonPaint = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint buttonBorder = new Paint(Paint.ANTI_ALIAS_FLAG);

    private final Handler handler = new Handler(Looper.getMainLooper());
    private final SparseArray<Point> active = new SparseArray<>();
    private final List<Ripple> ripples = new ArrayList<>();
    private final RectF sensor = new RectF();
    private final RectF[] buttons = {new RectF(), new RectF(), new RectF()};

    private final float density;
    private final RectF panel = new RectF();
    private float corner;
    private Shader panelShader;
    private Shader sensorShader;

    private ConnectionManager connection;
    private float sensitivity = 1f;
    private float scrollSpeed = 1f;
    private boolean naturalScroll = true;
    private boolean haptics = true;

    private int mode = MODE_IDLE;
    private int maxPointers;
    private int dragPointerId = -1;
    private int buttonPointer = -1;
    private int buttonIndex = -1;
    private long downTime;
    private long lastTapTime;
    private float lastTapX, lastTapY;
    private float downX, downY, lastX, lastY;
    private float totalMove;
    private boolean moved;
    private boolean leftDown;
    private boolean scrollHapticSent;
    private boolean pendingDoubleDrag;
    private float lastCentroidX, lastCentroidY;
    private boolean centroidReady;
    private float scrollAccX, scrollAccY;
    private final float touchSlop;

    private final Runnable longPress = () -> {
        if (mode == MODE_ONE && !moved && active.size() == 1) {
            beginDrag();
        }
    };

    public TouchpadView(Context context) {
        this(context, null);
    }

    public TouchpadView(Context context, AttributeSet attrs) {
        super(context, attrs);
        density = getResources().getDisplayMetrics().density;
        touchSlop = TAP_SLOP_DP * density;
        setClickable(true);
        setFocusable(true);
        setContentDescription("触摸板，支持轻点、双指滚动与拖拽");
        setupPaints();
    }

    public void setConnection(ConnectionManager connection) { this.connection = connection; }
    public void setSensitivity(float sensitivity) { this.sensitivity = clamp(sensitivity, 0.35f, 2.5f); }
    public void setScrollSpeed(float scrollSpeed) { this.scrollSpeed = clamp(scrollSpeed, 0.4f, 2.2f); }
    public void setNaturalScroll(boolean natural) { this.naturalScroll = natural; }
    public void setHaptics(boolean enabled) { this.haptics = enabled; }

    private float dp(float v) { return v * density; }
    private static float clamp(float v, float lo, float hi) { return Math.max(lo, Math.min(hi, v)); }

    private void setupPaints() {
        borderPaint.setStyle(Paint.Style.STROKE);
        borderPaint.setStrokeWidth(dp(1));
        sensorBorder.setStyle(Paint.Style.STROKE);
        sensorBorder.setStrokeWidth(dp(1));
        textPaint.setTypeface(Typeface.create("sans-serif-medium", Typeface.NORMAL));
        textPaint.setLetterSpacing(0.12f);
        hintPaint.setTypeface(Typeface.create("sans-serif", Typeface.NORMAL));
        touchPaint.setStyle(Paint.Style.FILL);
        touchCorePaint.setStyle(Paint.Style.FILL);
        ripplePaint.setStyle(Paint.Style.STROKE);
        ripplePaint.setStrokeWidth(dp(1.4f));
        chipPaint.setStyle(Paint.Style.FILL);
        buttonPaint.setStyle(Paint.Style.FILL);
        buttonBorder.setStyle(Paint.Style.STROKE);
        buttonBorder.setStrokeWidth(dp(1));
    }

    @Override
    protected void onSizeChanged(int w, int h, int oldw, int oldh) {
        super.onSizeChanged(w, h, oldw, oldh);
        panel.set(dp(2), dp(2), w - dp(2), h - dp(2));
        corner = dp(22);
        panelShader = new LinearGradient(0, panel.top, 0, panel.bottom,
                0xFF0E141D, 0xFF080C12, Shader.TileMode.CLAMP);

        float side = dp(14);
        float top = panel.top + dp(44);
        float bottom = panel.bottom - dp(112);
        sensor.set(panel.left + side, top, panel.right - side, Math.max(top + dp(80), bottom));
        sensorShader = new LinearGradient(0, sensor.top, 0, sensor.bottom,
                0xFF0A0F17, 0xFF080B10, Shader.TileMode.CLAMP);

        float gap = dp(8);
        float bh = dp(44);
        float bw = (sensor.width() - gap * 2) / 3f;
        float by = panel.bottom - dp(58);
        for (int i = 0; i < 3; i++) {
            float left = sensor.left + i * (bw + gap);
            buttons[i].set(left, by, left + bw, by + bh);
        }
    }

    @Override
    protected void onDraw(Canvas canvas) {
        super.onDraw(canvas);
        if (panel.width() <= 0) return;

        panelPaint.setShader(panelShader);
        canvas.drawRoundRect(panel, corner, corner, panelPaint);
        borderPaint.setColor(0xFF1B2634);
        canvas.drawRoundRect(panel, corner, corner, borderPaint);

        sensorPaint.setShader(sensorShader);
        canvas.drawRoundRect(sensor, dp(18), dp(18), sensorPaint);
        sensorBorder.setColor(0xFF17212E);
        canvas.drawRoundRect(sensor, dp(18), dp(18), sensorBorder);

        drawGrid(canvas);
        drawHeader(canvas);
        drawHints(canvas);
        drawStateChip(canvas);
        drawTouches(canvas);
        drawButtons(canvas);
        drawRipples(canvas);
    }

    private void drawGrid(Canvas canvas) {
        dotPaint.setColor(0x2489A0B8);
        float left = sensor.left + dp(18);
        float right = sensor.right - dp(18);
        float top = sensor.top + dp(20);
        float bottom = sensor.bottom - dp(18);
        float spacing = dp(25);
        for (float y = top; y < bottom; y += spacing) {
            for (float x = left; x < right; x += spacing) {
                canvas.drawCircle(x, y, dp(0.9f), dotPaint);
            }
        }
    }

    private void drawHeader(Canvas canvas) {
        textPaint.setTextSize(dp(11.5f));
        textPaint.setColor(0xFF78E4FA);
        textPaint.setTextAlign(Paint.Align.LEFT);
        canvas.drawText("TOUCHPAD", panel.left + dp(20), panel.top + dp(27), textPaint);

        hintPaint.setTextSize(dp(9));
        hintPaint.setColor(0xFF58677C);
        hintPaint.setTextAlign(Paint.Align.RIGHT);
        canvas.drawText("USB HID", panel.right - dp(20), panel.top + dp(27), hintPaint);
    }

    private void drawHints(Canvas canvas) {
        hintPaint.setTextSize(dp(9.5f));
        hintPaint.setColor(0xFF6E7D92);
        hintPaint.setTextAlign(Paint.Align.LEFT);
        float x = sensor.left + dp(4);
        float y = panel.bottom - dp(78);
        canvas.drawText("轻点 左键 · 双指轻点 右键", x, y, hintPaint);
        canvas.drawText("双指滑动 滚动 · 双击后拖动", x, y + dp(14), hintPaint);
    }

    private void drawStateChip(Canvas canvas) {
        String label = null;
        if (mode == MODE_DRAG) label = "DRAG";
        else if (mode == MODE_TWO) label = "SCROLL";
        else if (mode == MODE_THREE) label = "3F";
        if (label == null) return;

        hintPaint.setTextSize(dp(8));
        hintPaint.setTextAlign(Paint.Align.LEFT);
        float tw = hintPaint.measureText(label);
        float pad = dp(7);
        float right = sensor.right - dp(14);
        float left = right - tw - pad * 2;
        float top = sensor.top + dp(12);
        RectF chip = new RectF(left, top, right, top + dp(18));
        chipPaint.setColor(0x3328C9E8);
        canvas.drawRoundRect(chip, dp(9), dp(9), chipPaint);
        hintPaint.setColor(0xFF63E3FF);
        canvas.drawText(label, left + pad, top + dp(12.5f), hintPaint);
    }

    private void drawTouches(Canvas canvas) {
        for (int i = 0; i < active.size(); i++) {
            Point p = active.valueAt(i);
            float r = dp(19);
            touchPaint.setColor(0x2263E3FF);
            canvas.drawCircle(p.x, p.y, r, touchPaint);
            touchPaint.setColor(0x3363E3FF);
            canvas.drawCircle(p.x, p.y, r * 0.62f, touchPaint);
            touchCorePaint.setColor(0xCCBFF7FF);
            canvas.drawCircle(p.x, p.y, dp(3.2f), touchCorePaint);
        }
    }

    private void drawButtons(Canvas canvas) {
        for (int i = 0; i < 3; i++) {
            RectF r = buttons[i];
            boolean pressed = mode == MODE_BUTTON && buttonIndex == i;
            buttonPaint.setColor(pressed ? 0xFF63E3FF : 0xFF141C27);
            canvas.drawRoundRect(r, dp(12), dp(12), buttonPaint);
            buttonBorder.setColor(pressed ? 0xFFC8F7FF : 0xFF263345);
            canvas.drawRoundRect(r, dp(12), dp(12), buttonBorder);

            textPaint.setTextSize(dp(12));
            textPaint.setTextAlign(Paint.Align.CENTER);
            textPaint.setColor(pressed ? 0xFF041016 : 0xFFC6D5E8);
            canvas.drawText(BUTTON_LABELS[i], r.centerX(), r.centerY() - dp(2), textPaint);

            hintPaint.setTextSize(dp(7.5f));
            hintPaint.setTextAlign(Paint.Align.CENTER);
            hintPaint.setColor(pressed ? 0xCC041016 : 0xFF6E7D92);
            canvas.drawText(BUTTON_SUBS[i], r.centerX(), r.centerY() + dp(10), hintPaint);
        }
        hintPaint.setTextAlign(Paint.Align.LEFT);
    }

    private void drawRipples(Canvas canvas) {
        long now = SystemClock.uptimeMillis();
        boolean alive = false;
        Iterator<Ripple> it = ripples.iterator();
        while (it.hasNext()) {
            Ripple r = it.next();
            float t = (now - r.start) / 420f;
            if (t >= 1f) { it.remove(); continue; }
            alive = true;
            float radius = dp(12) + dp(34) * t;
            ripplePaint.setColor(((int) (150 * (1 - t)) << 24) | 0x63E3FF);
            canvas.drawCircle(r.x, r.y, radius, ripplePaint);
            if (r.label != null && t < 0.75f) {
                hintPaint.setTextAlign(Paint.Align.CENTER);
                hintPaint.setTextSize(dp(9));
                hintPaint.setColor(((int) (230 * (1 - t)) << 24) | 0xDFF9FF);
                canvas.drawText(r.label, r.x, r.y + dp(3), hintPaint);
                hintPaint.setTextAlign(Paint.Align.LEFT);
            }
        }
        if (alive) postInvalidateOnAnimation();
    }

    @Override
    public boolean onTouchEvent(MotionEvent event) {
        int action = event.getActionMasked();
        switch (action) {
            case MotionEvent.ACTION_DOWN: {
                resetForNewGesture();
                trackPointers(event);
                int hit = hitButton(event.getX(0), event.getY(0));
                if (hit >= 0) {
                    startButton(hit, event.getPointerId(0));
                    return true;
                }
                startOneFinger(event);
                break;
            }
            case MotionEvent.ACTION_POINTER_DOWN: {
                if (mode == MODE_BUTTON) break;
                trackPointers(event);
                maxPointers = Math.max(maxPointers, event.getPointerCount());
                handler.removeCallbacks(longPress);
                if (mode == MODE_ONE) {
                    mode = MODE_TWO;
                    centroidReady = false;
                    scrollAccX = 0;
                    scrollAccY = 0;
                } else if (mode == MODE_TWO) {
                    mode = MODE_THREE;
                }
                invalidate();
                break;
            }
            case MotionEvent.ACTION_MOVE:
                trackPointers(event);
                if (mode != MODE_BUTTON) handleMove(event);
                invalidate();
                break;
            case MotionEvent.ACTION_POINTER_UP: {
                int idx = event.getActionIndex();
                int id = event.getPointerId(idx);
                if (mode == MODE_BUTTON) {
                    if (id == buttonPointer) endButton();
                    active.remove(id);
                    invalidate();
                    break;
                }
                finishPointer(id, event.getPointerCount() - 1, event);
                active.remove(id);
                invalidate();
                break;
            }
            case MotionEvent.ACTION_UP: {
                int id = event.getPointerId(0);
                if (mode == MODE_BUTTON) {
                    endButton();
                    active.clear();
                    invalidate();
                    break;
                }
                finishPointer(id, 0, event);
                active.clear();
                invalidate();
                break;
            }
            case MotionEvent.ACTION_CANCEL:
                if (mode == MODE_BUTTON) endButton();
                cancelGesture();
                break;
            default:
                break;
        }
        return true;
    }

    private int hitButton(float x, float y) {
        for (int i = 0; i < 3; i++) {
            if (buttons[i].contains(x, y)) return i;
        }
        return -1;
    }

    private void startButton(int index, int pointerId) {
        buttonIndex = index;
        buttonPointer = pointerId;
        mode = MODE_BUTTON;
        if (connection != null) connection.sendButton(BUTTON_NAMES[index], true);
        haptic(HapticFeedbackConstants.VIRTUAL_KEY);
        invalidate();
    }

    private void endButton() {
        if (buttonIndex >= 0 && connection != null) {
            connection.sendButton(BUTTON_NAMES[buttonIndex], false);
        }
        buttonIndex = -1;
        buttonPointer = -1;
    }

    private void resetForNewGesture() {
        active.clear();
        mode = MODE_ONE;
        maxPointers = 1;
        moved = false;
        leftDown = false;
        dragPointerId = -1;
        pendingDoubleDrag = false;
        buttonPointer = -1;
        buttonIndex = -1;
        totalMove = 0;
        scrollAccX = 0;
        scrollAccY = 0;
        scrollHapticSent = false;
        pendingDoubleDrag = false;
        centroidReady = false;
        downTime = SystemClock.uptimeMillis();
        handler.removeCallbacks(longPress);
    }

    private void startOneFinger(MotionEvent event) {
        downX = event.getX(0);
        downY = event.getY(0);
        lastX = downX;
        lastY = downY;
        handler.postDelayed(longPress, LONG_PRESS_TIMEOUT);

        long now = SystemClock.uptimeMillis();
        float dx = downX - lastTapX;
        float dy = downY - lastTapY;
        if (now - lastTapTime < DOUBLE_TAP_TIMEOUT
                && dx * dx + dy * dy < dp(DOUBLE_TAP_SLOP_DP) * dp(DOUBLE_TAP_SLOP_DP)) {
            pendingDoubleDrag = true;
        }
    }

    private void handleMove(MotionEvent event) {
        if (mode == MODE_ONE) {
            float x = event.getX(0);
            float y = event.getY(0);
            float dx = x - lastX;
            float dy = y - lastY;
            totalMove += (float) Math.hypot(dx, dy);
            if (!moved) {
                if (totalMove < touchSlop) {
                    lastX = x;
                    lastY = y;
                    return;
                }
                moved = true;
                handler.removeCallbacks(longPress);
                if (pendingDoubleDrag) {
                    pendingDoubleDrag = false;
                    beginDrag();
                }
                dx = x - downX;
                dy = y - downY;
            }
            lastX = x;
            lastY = y;
            sendMove(dx, dy);
        } else if (mode == MODE_TWO) {
            if (event.getPointerCount() < 2) return;
            float cx = (event.getX(0) + event.getX(1)) / 2f;
            float cy = (event.getY(0) + event.getY(1)) / 2f;
            if (!centroidReady) {
                lastCentroidX = cx;
                lastCentroidY = cy;
                centroidReady = true;
                return;
            }
            float dx = cx - lastCentroidX;
            float dy = cy - lastCentroidY;
            lastCentroidX = cx;
            lastCentroidY = cy;
            totalMove += (float) Math.hypot(dx, dy);
            if (!moved) {
                if (totalMove < touchSlop * 1.2f) return;
                moved = true;
            }
            queueScroll(dx, dy);
        } else if (mode == MODE_DRAG) {
            int idx = event.findPointerIndex(dragPointerId);
            if (idx < 0) idx = 0;
            float x = event.getX(idx);
            float y = event.getY(idx);
            float dx = x - lastX;
            float dy = y - lastY;
            lastX = x;
            lastY = y;
            sendMove(dx, dy);
        }
    }

    private void finishPointer(int id, int remaining, MotionEvent event) {
        handler.removeCallbacks(longPress);
        long now = SystemClock.uptimeMillis();
        long duration = now - downTime;

        if (mode == MODE_DRAG && id == dragPointerId) {
            endDrag();
            reset();
            return;
        }

        if (remaining > 0) return;

        if (mode == MODE_ONE) {
            if (!moved && duration < TAP_TIMEOUT) {
                pendingDoubleDrag = false;
                leftClick();
                addRipple(downX, downY, "L");
                lastTapTime = now;
                lastTapX = downX;
                lastTapY = downY;
            } else {
                lastTapTime = 0;
            }
        } else if (mode == MODE_TWO && maxPointers == 2) {
            if (!moved && duration < TAP_TIMEOUT + 80) {
                rightClick();
                addRipple(downX, downY, "R");
            }
            lastTapTime = 0;
        } else if (mode == MODE_THREE && maxPointers >= 3) {
            if (!moved && duration < TAP_TIMEOUT + 100) {
                middleClick();
                addRipple(downX, downY, "M");
            }
            lastTapTime = 0;
        }
        reset();
    }

    private void reset() {
        mode = MODE_IDLE;
        maxPointers = 0;
        moved = false;
        leftDown = false;
        dragPointerId = -1;
        buttonPointer = -1;
        buttonIndex = -1;
        active.clear();
        handler.removeCallbacks(longPress);
    }

    private void beginDrag() {
        if (leftDown || mode != MODE_ONE) return;
        int key = active.size() > 0 ? active.keyAt(0) : -1;
        dragPointerId = key;
        leftDown = true;
        mode = MODE_DRAG;
        if (connection != null) connection.sendButton("left", true);
        haptic(HapticFeedbackConstants.LONG_PRESS);
        addRipple(downX, downY, "DRAG");
        invalidate();
    }

    private void moveToDrag() {
        if (mode != MODE_ONE) return;
        handler.removeCallbacks(longPress);
        beginDrag();
    }

    private void endDrag() {
        if (leftDown) {
            if (connection != null) connection.sendButton("left", false);
            leftDown = false;
        }
        lastTapTime = 0;
    }

    private void cancelGesture() {
        endDrag();
        reset();
        invalidate();
    }

    private void leftClick() {
        if (connection == null) return;
        connection.sendButton("left", true);
        connection.sendButton("left", false);
        haptic(HapticFeedbackConstants.VIRTUAL_KEY);
    }

    private void rightClick() {
        if (connection == null) return;
        connection.sendButton("right", true);
        connection.sendButton("right", false);
        haptic(HapticFeedbackConstants.VIRTUAL_KEY);
    }

    private void middleClick() {
        if (connection == null) return;
        connection.sendButton("middle", true);
        connection.sendButton("middle", false);
        haptic(HapticFeedbackConstants.VIRTUAL_KEY);
    }

    private void sendMove(float dx, float dy) {
        if (connection == null) return;
        float gain = 2.15f * sensitivity / density;
        connection.sendMove(dx * gain, dy * gain);
    }

    private void queueScroll(float dx, float dy) {
        if (connection == null) return;
        float step = Math.max(dp(8), dp(SCROLL_STEP_DP) / scrollSpeed);
        scrollAccX += dx;
        scrollAccY += dy;
        int nx = (int) (scrollAccX / step);
        int ny = (int) (scrollAccY / step);
        if (nx == 0 && ny == 0) return;
        scrollAccX -= nx * step;
        scrollAccY -= ny * step;
        // Natural: content follows the fingers. Horizontal wheel has the
        // opposite sign convention from vertical wheel on Windows.
        int outX = naturalScroll ? -nx : nx;
        int outY = naturalScroll ? ny : -ny;
        connection.sendScroll(outX, outY);
        if (!scrollHapticSent) {
            scrollHapticSent = true;
            haptic(HapticFeedbackConstants.CLOCK_TICK);
        }
    }

    private void trackPointers(MotionEvent event) {
        for (int i = 0; i < event.getPointerCount(); i++) {
            int id = event.getPointerId(i);
            Point p = active.get(id);
            if (p == null) {
                p = new Point();
                active.put(id, p);
            }
            p.x = event.getX(i);
            p.y = event.getY(i);
        }
    }

    private void haptic(int constant) {
        if (haptics) performHapticFeedback(constant);
    }

    private void addRipple(float x, float y, String label) {
        ripples.add(new Ripple(x, y, SystemClock.uptimeMillis(), label));
        invalidate();
    }

    /** Release a drag/button and clear pending gestures. */
    public void releaseAll() {
        endButton();
        endDrag();
        reset();
        invalidate();
    }

    @Override
    protected void onDetachedFromWindow() {
        releaseAll();
        super.onDetachedFromWindow();
    }

    private static final class Point {
        float x;
        float y;
    }

    private static final class Ripple {
        final float x;
        final float y;
        final long start;
        final String label;
        Ripple(float x, float y, long start, String label) {
            this.x = x;
            this.y = y;
            this.start = start;
            this.label = label;
        }
    }
}
