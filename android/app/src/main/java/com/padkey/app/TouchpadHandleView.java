package com.padkey.app;

import android.content.Context;
import android.graphics.Canvas;
import android.graphics.Paint;
import android.graphics.Path;
import android.graphics.RectF;
import android.util.AttributeSet;
import android.view.HapticFeedbackConstants;
import android.view.View;

/** Slim always-visible tab used to collapse / reveal the touchpad. */
public class TouchpadHandleView extends View {
    private final Paint fill = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint stroke = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint chevron = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint dot = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final RectF rect = new RectF();
    private final Path path = new Path();
    private final float density;
    private boolean collapsed;

    public TouchpadHandleView(Context context) { this(context, null); }

    public TouchpadHandleView(Context context, AttributeSet attrs) {
        super(context, attrs);
        density = getResources().getDisplayMetrics().density;
        setClickable(true);
        setFocusable(true);
        setContentDescription("隐藏或显示触摸板");
        fill.setStyle(Paint.Style.FILL);
        stroke.setStyle(Paint.Style.STROKE);
        stroke.setStrokeWidth(dp(1));
        chevron.setStyle(Paint.Style.STROKE);
        chevron.setStrokeWidth(dp(2));
        chevron.setStrokeCap(Paint.Cap.ROUND);
        chevron.setStrokeJoin(Paint.Join.ROUND);
        dot.setStyle(Paint.Style.FILL);
    }

    private float dp(float v) { return v * density; }

    public void setCollapsed(boolean collapsed) {
        this.collapsed = collapsed;
        setContentDescription(collapsed ? "展开触摸板" : "收起触摸板");
        invalidate();
    }

    public boolean isCollapsed() { return collapsed; }

    @Override
    protected void onDraw(Canvas canvas) {
        super.onDraw(canvas);
        float w = getWidth();
        float h = getHeight();
        float inset = dp(3);
        rect.set(inset, dp(8), w - inset, h - dp(8));
        float radius = rect.width() / 2f;

        fill.setColor(isPressed() ? 0xFF63E3FF : 0xFF101721);
        canvas.drawRoundRect(rect, radius, radius, fill);
        stroke.setColor(isPressed() ? 0xFFC8F7FF : 0xFF263345);
        canvas.drawRoundRect(rect, radius, radius, stroke);

        float cx = rect.centerX();
        float cy = rect.centerY();
        float size = dp(4.5f);
        boolean pointLeft = collapsed;
        path.reset();
        if (pointLeft) {
            path.moveTo(cx + size * 0.55f, cy - size);
            path.lineTo(cx - size * 0.55f, cy);
            path.lineTo(cx + size * 0.55f, cy + size);
        } else {
            path.moveTo(cx - size * 0.55f, cy - size);
            path.lineTo(cx + size * 0.55f, cy);
            path.lineTo(cx - size * 0.55f, cy + size);
        }
        chevron.setColor(isPressed() ? 0xFF041016 : 0xFF8BEEFF);
        canvas.drawPath(path, chevron);

        dot.setColor(isPressed() ? 0x88041016 : 0x6674879D);
        canvas.drawCircle(cx, rect.top + dp(14), dp(1.4f), dot);
        canvas.drawCircle(cx, rect.bottom - dp(14), dp(1.4f), dot);
    }

    @Override
    public boolean performClick() {
        performHapticFeedback(HapticFeedbackConstants.VIRTUAL_KEY);
        return super.performClick();
    }
}
