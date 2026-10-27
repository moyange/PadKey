package com.padkey.app;

import android.app.Activity;
import android.app.AlertDialog;
import android.content.Intent;
import android.content.SharedPreferences;
import android.content.res.ColorStateList;
import android.graphics.Color;
import android.graphics.drawable.GradientDrawable;
import android.os.Build;
import android.os.Bundle;
import android.text.InputType;
import android.view.Gravity;
import android.view.KeyEvent;
import android.view.View;
import android.view.WindowInsets;
import android.view.WindowInsetsController;
import android.view.WindowManager;
import android.widget.Button;
import android.widget.EditText;
import android.widget.ImageButton;
import android.widget.LinearLayout;
import android.widget.ScrollView;
import android.widget.SeekBar;
import android.widget.Switch;
import android.widget.TextView;
import android.widget.Toast;

import java.util.List;
import java.util.Locale;

public class MainActivity extends Activity implements ConnectionManager.Listener {
    private static final String PREFS = "padkey";
    private static final String P_HOST = "host";
    private static final String P_PORT = "port";
    private static final String P_TOKEN = "token";
    private static final String P_SENS = "sensitivity";
    private static final String P_SCROLL = "scroll";
    private static final String P_HAPTICS = "haptics";
    private static final String P_NATURAL = "natural";
    private static final String P_TOUCHPAD_HIDDEN = "touchpadHidden";
    private static final String P_WAKE_MACS = "wakeMacs";
    private static final String P_BT_KEYBOARD = "btKeyboard";

    private ConnectionManager connection;
    private KeyboardView keyboard;
    private TouchpadView touchpad;
    private TouchpadHandleView touchpadHandle;
    private TextView statusText;
    private TextView rttText;
    private View statusDot;
    private ImageButton settingsButton;
    private ImageButton powerButton;
    private SharedPreferences prefs;

    private String host;
    private int port;
    private String token;
    private float sensitivity;
    private float scrollSpeed;
    private boolean haptics;
    private boolean naturalScroll;
    private boolean touchpadHidden;
    private String linkLabel = "USB";
    private String pcName = "PC";
    private boolean pcLocked;
    private String[] wakeMacs = new String[0];
    private boolean btKeyboardEnabled;
    private boolean settingsOpen;
    private boolean resumed;

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        getWindow().addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);
        if (Build.VERSION.SDK_INT >= 28) {
            WindowManager.LayoutParams lp = getWindow().getAttributes();
            lp.layoutInDisplayCutoutMode = WindowManager.LayoutParams.LAYOUT_IN_DISPLAY_CUTOUT_MODE_SHORT_EDGES;
        }
        setContentView(R.layout.activity_main);

        prefs = getSharedPreferences(PREFS, MODE_PRIVATE);
        loadSettings();
        applyIntentExtras(getIntent());

        keyboard = findViewById(R.id.keyboardView);
        touchpad = findViewById(R.id.touchpadView);
        touchpadHandle = findViewById(R.id.touchpadHandle);
        statusText = findViewById(R.id.statusText);
        rttText = findViewById(R.id.rttText);
        statusDot = findViewById(R.id.statusDot);
        settingsButton = findViewById(R.id.settingsButton);
        powerButton = findViewById(R.id.powerButton);

        connection = new ConnectionManager(this);
        connection.configure(host, port, token);
        keyboard.setConnection(connection);
        keyboard.setHaptics(haptics);
        touchpad.setConnection(connection);
        touchpad.setSensitivity(sensitivity);
        touchpad.setScrollSpeed(scrollSpeed);
        touchpad.setHaptics(haptics);
        touchpad.setNaturalScroll(naturalScroll);

        touchpadHidden = prefs.getBoolean(P_TOUCHPAD_HIDDEN, false);
        touchpadHandle.setCollapsed(touchpadHidden);
        touchpadHandle.setOnClickListener(v -> toggleTouchpad());
        if (touchpadHidden) touchpad.setVisibility(View.GONE);

        settingsButton.setOnClickListener(v -> showSettings());
        powerButton.setOnClickListener(v -> showPowerDialog());
        setConnectedUi(false, "未连接");
    }

    @Override
    protected void onResume() {
        super.onResume();
        resumed = true;
        hideSystemUi();
        connection.configure(host, port, token);
        connection.start();
    }

    @Override
    protected void onPause() {
        resumed = false;
        if (keyboard != null) keyboard.releaseAll();
        if (touchpad != null) touchpad.releaseAll();
        if (connection != null) {
            connection.releaseAll();
            connection.stop();
        }
        super.onPause();
    }

    @Override
    protected void onDestroy() {
        if (connection != null) connection.stop();
        super.onDestroy();
    }

    @Override
    public void onWindowFocusChanged(boolean hasFocus) {
        super.onWindowFocusChanged(hasFocus);
        if (hasFocus) hideSystemUi();
    }

    @Override
    protected void onNewIntent(Intent intent) {
        super.onNewIntent(intent);
        setIntent(intent);
        if (applyIntentExtras(intent)) {
            saveSettings();
            if (connection != null) {
                connection.stop();
                connection.configure(host, port, token);
                connection.start();
            }
        }
    }

    private boolean applyIntentExtras(Intent intent) {
        if (intent == null) return false;
        boolean changed = false;
        String h = intent.getStringExtra("host");
        int p = intent.getIntExtra("port", -1);
        String t = intent.getStringExtra("token");
        if (h != null && !h.trim().isEmpty()) { host = h.trim(); changed = true; }
        if (p > 0 && p <= 65535) { port = p; changed = true; }
        if (t != null) { token = t; changed = true; }
        if (changed) saveSettings();
        return changed;
    }

    private void hideSystemUi() {
        if (Build.VERSION.SDK_INT >= 30) {
            getWindow().setDecorFitsSystemWindows(false);
            WindowInsetsController controller = getWindow().getInsetsController();
            if (controller != null) {
                controller.hide(WindowInsets.Type.statusBars() | WindowInsets.Type.navigationBars());
                controller.setSystemBarsBehavior(WindowInsetsController.BEHAVIOR_SHOW_TRANSIENT_BARS_BY_SWIPE);
            }
        } else {
            getWindow().getDecorView().setSystemUiVisibility(
                    View.SYSTEM_UI_FLAG_IMMERSIVE_STICKY
                            | View.SYSTEM_UI_FLAG_LAYOUT_STABLE
                            | View.SYSTEM_UI_FLAG_LAYOUT_HIDE_NAVIGATION
                            | View.SYSTEM_UI_FLAG_LAYOUT_FULLSCREEN
                            | View.SYSTEM_UI_FLAG_HIDE_NAVIGATION
                            | View.SYSTEM_UI_FLAG_FULLSCREEN);
        }
    }

    @Override
    public boolean dispatchKeyEvent(KeyEvent event) {
        if (btKeyboardEnabled && !settingsOpen && connection != null && connection.isConnected()) {
            int hid = AndroidKeyMap.toHid(event.getKeyCode());
            if (hid != 0) {
                if (event.getAction() == KeyEvent.ACTION_DOWN) {
                    if (event.getRepeatCount() == 0) connection.sendKey(hid, true);
                } else if (event.getAction() == KeyEvent.ACTION_UP) {
                    connection.sendKey(hid, false);
                }
                return true;
            }
        }
        return super.dispatchKeyEvent(event);
    }

    // ---- ConnectionManager.Listener -----------------------------------------

    @Override
    public void onState(ConnectionManager.State state, String detail) {
        runOnUiThread(() -> {
            switch (state) {
                case CONNECTED:
                    setConnectedUi(true, "正在同步…");
                    break;
                case CONNECTING:
                    setConnectedUi(false, "正在连接 " + host + ":" + port);
                    break;
                default:
                    pcLocked = false;
                    setConnectedUi(false, "未连接 · 等待电脑端");
                    rttText.setText("");
                    break;
            }
        });
    }

    @Override
    public void onRtt(long rttMs) {
        runOnUiThread(() -> rttText.setText(rttMs + " ms"));
    }

    @Override
    public void onWelcome(String pc, String link) {
        runOnUiThread(() -> {
            pcName = pc == null || pc.isEmpty() ? "PC" : pc;
            linkLabel = link == null || link.isEmpty() ? "USB" : link;
            showConnectedHeader();
            if (connection.getLastRtt() >= 0) rttText.setText(connection.getLastRtt() + " ms");
        });
    }

    @Override
    public void onLocked(boolean locked) {
        runOnUiThread(() -> {
            boolean wasLocked = pcLocked;
            pcLocked = locked;
            if (locked && !wasLocked) {
                Toast.makeText(MainActivity.this,
                        "电脑已锁屏，安全输入服务未就绪；请先在电脑上解锁。",
                        Toast.LENGTH_LONG).show();
            }
            if (connection != null && connection.isConnected()) showConnectedHeader();
        });
    }

    @Override
    public void onWakeInfo(String[] macs) {
        runOnUiThread(() -> {
            if (macs == null) return;
            java.util.ArrayList<String> valid = new java.util.ArrayList<>();
            for (String mac : macs) {
                if (mac != null && mac.matches("^[0-9A-Fa-f]{2}([:-][0-9A-Fa-f]{2}){5}$")) valid.add(mac);
            }
            wakeMacs = valid.toArray(new String[0]);
            prefs.edit().putString(P_WAKE_MACS, String.join(",", wakeMacs)).apply();
        });
    }

    @Override
    public void onPowerResult(boolean ok, String message) {
        runOnUiThread(() -> {
            if (ok) Toast.makeText(this, "已请求电脑休眠", Toast.LENGTH_SHORT).show();
            else Toast.makeText(this, "电源操作失败: " + message, Toast.LENGTH_LONG).show();
        });
    }

    private void showPowerDialog() {
        new AlertDialog.Builder(this)
                .setTitle("电脑电源")
                .setItems(new CharSequence[]{"让电脑休眠", "唤醒电脑"}, (dialog, which) -> {
                    if (which == 0) confirmSleep();
                    else wakePc();
                })
                .setNegativeButton("取消", null)
                .show();
    }

    private void confirmSleep() {
        if (connection == null || !connection.isConnected()) {
            Toast.makeText(this, "电脑未连接，无法休眠", Toast.LENGTH_SHORT).show();
            return;
        }
        new AlertDialog.Builder(this)
                .setTitle("电脑休眠")
                .setMessage("电脑将立即进入睡眠。唤醒需要电脑支持 Wake-on-LAN，并且平板仍在同一 Wi-Fi。")
                .setPositiveButton("休眠", (dialog, which) -> {
                    connection.sendPower("sleep");
                    Toast.makeText(this, "已发送休眠请求", Toast.LENGTH_SHORT).show();
                })
                .setNegativeButton("取消", null)
                .show();
    }

    private void wakePc() {
        if (wakeMacs.length == 0) {
            Toast.makeText(this, "还没有电脑网卡地址，请先连接一次 PadKey", Toast.LENGTH_LONG).show();
            return;
        }
        String targetHost = host;
        new Thread(() -> {
            int sent = WakeOnLan.wake(targetHost, wakeMacs);
            runOnUiThread(() -> Toast.makeText(this,
                    sent > 0 ? "已发送 Wake-on-LAN 唤醒包" : "唤醒包发送失败，请确认平板连接 Wi-Fi",
                    Toast.LENGTH_LONG).show());
        }, "padkey-wol").start();
    }

    private void showConnectedHeader() {
        if (pcLocked) {
            setDot(0xFFFFB454);
            statusText.setText("已连接 · 电脑已锁屏");
            statusText.setTextColor(0xFFFFD08A);
        } else {
            String state = linkLabel.equals("LAN") ? "局域网已连接" : "USB 已连接";
            setConnectedUi(true, state + " · " + pcName);
        }
    }

    private void setConnectedUi(boolean connected, String text) {
        int color;
        if (connected) color = 0xFF59E0A0;
        else if (text.startsWith("正在")) color = 0xFFFFB454;
        else color = 0xFF5A6475;

        setDot(color);
        statusText.setText(text);
        statusText.setTextColor(connected ? 0xFFD8E4F2 : 0xFF8794A8);
        if (!connected) rttText.setText("");
    }

    private int dpInt(float v) {
        return Math.round(v * getResources().getDisplayMetrics().density);
    }

    private void toggleTouchpad() {
        touchpadHidden = !touchpadHidden;
        prefs.edit().putBoolean(P_TOUCHPAD_HIDDEN, touchpadHidden).apply();
        touchpadHandle.setCollapsed(touchpadHidden);

        if (touchpadHidden) {
            touchpad.releaseAll();
            touchpad.animate().cancel();
            int w = touchpad.getWidth() > 0 ? touchpad.getWidth() : dpInt(120);
            touchpad.animate()
                    .alpha(0f)
                    .translationX(w)
                    .setDuration(160)
                    .withEndAction(() -> {
                        if (!touchpadHidden) return;
                        touchpad.setVisibility(View.GONE);
                        touchpad.setAlpha(1f);
                        touchpad.setTranslationX(0f);
                    })
                    .start();
        } else {
            touchpad.animate().cancel();
            touchpad.setVisibility(View.VISIBLE);
            touchpad.setAlpha(0f);
            touchpad.post(() -> {
                touchpad.setTranslationX(touchpad.getWidth());
                touchpad.animate()
                        .alpha(1f)
                        .translationX(0f)
                        .setDuration(180)
                        .start();
            });
        }
    }

    private void setDot(int color) {
        GradientDrawable dot = new GradientDrawable();
        dot.setShape(GradientDrawable.OVAL);
        dot.setColor(color);
        statusDot.setBackground(dot);
    }

    // ---- Settings ------------------------------------------------------------

    private void loadSettings() {
        host = prefs.getString(P_HOST, "127.0.0.1");
        port = prefs.getInt(P_PORT, 8765);
        token = prefs.getString(P_TOKEN, "");
        String savedMacs = prefs.getString(P_WAKE_MACS, "");
        wakeMacs = savedMacs.isEmpty() ? new String[0] : savedMacs.split(",");
        sensitivity = prefs.getFloat(P_SENS, 1.0f);
        scrollSpeed = prefs.getFloat(P_SCROLL, 1.0f);
        haptics = prefs.getBoolean(P_HAPTICS, true);
        naturalScroll = prefs.getBoolean(P_NATURAL, true);
        btKeyboardEnabled = prefs.getBoolean(P_BT_KEYBOARD, true);
    }

    private void saveSettings() {
        prefs.edit()
                .putString(P_HOST, host)
                .putInt(P_PORT, port)
                .putString(P_TOKEN, token)
                .putFloat(P_SENS, sensitivity)
                .putFloat(P_SCROLL, scrollSpeed)
                .putBoolean(P_HAPTICS, haptics)
                .putBoolean(P_NATURAL, naturalScroll)
                .putBoolean(P_BT_KEYBOARD, btKeyboardEnabled)
                .apply();
    }

    private void applySettingsToViews() {
        keyboard.setHaptics(haptics);
        touchpad.setSensitivity(sensitivity);
        touchpad.setScrollSpeed(scrollSpeed);
        touchpad.setHaptics(haptics);
        touchpad.setNaturalScroll(naturalScroll);
    }

    private void showSettings() {
        if (connection != null) connection.releaseAll();
        float d = getResources().getDisplayMetrics().density;
        int pad = (int) (22 * d);

        LinearLayout root = new LinearLayout(this);
        root.setOrientation(LinearLayout.VERTICAL);
        root.setPadding(pad, (int) (6 * d), pad, 0);

        TextView intro = new TextView(this);
        intro.setText("USB 模式：电脑运行服务端后执行 scripts/connect-usb.ps1，App 会自动连接本机转发端口。\nWi-Fi 模式：服务端加 --lan 启动，填写电脑 IP、端口和 Token，或点下方扫描自动发现。");
        intro.setTextColor(0xFF8794A8);
        intro.setTextSize(12);
        intro.setLineSpacing(0, 1.25f);
        root.addView(intro, new LinearLayout.LayoutParams(
                LinearLayout.LayoutParams.MATCH_PARENT, LinearLayout.LayoutParams.WRAP_CONTENT));

        EditText hostEdit = new EditText(this);
        hostEdit.setHint("电脑 IP，例如 127.0.0.1");
        hostEdit.setText(host);
        hostEdit.setSingleLine(true);
        hostEdit.setInputType(InputType.TYPE_CLASS_TEXT | InputType.TYPE_TEXT_VARIATION_URI);
        addField(root, "主机", hostEdit, d);

        EditText portEdit = new EditText(this);
        portEdit.setHint("8765");
        portEdit.setText(String.valueOf(port));
        portEdit.setSingleLine(true);
        portEdit.setInputType(InputType.TYPE_CLASS_NUMBER);
        addField(root, "端口", portEdit, d);

        EditText tokenEdit = new EditText(this);
        tokenEdit.setHint("USB 模式可留空");
        tokenEdit.setText(token);
        tokenEdit.setSingleLine(true);
        tokenEdit.setInputType(InputType.TYPE_CLASS_TEXT | InputType.TYPE_TEXT_VARIATION_VISIBLE_PASSWORD);
        addField(root, "Token（局域网）", tokenEdit, d);

        Button scanButton = new Button(this);
        scanButton.setText("扫描同一 Wi-Fi 下的服务端");
        scanButton.setAllCaps(false);
        scanButton.setTextSize(12);
        scanButton.setTextColor(0xFF63E3FF);
        GradientDrawable scanBg = new GradientDrawable();
        scanBg.setColor(0xFF14212E);
        scanBg.setCornerRadius(12 * d);
        scanBg.setStroke((int) d, 0xFF2A3B4E);
        scanButton.setBackground(scanBg);
        scanButton.setOnClickListener(v -> {
            scanButton.setEnabled(false);
            scanButton.setText("扫描中…");
            WifiDiscovery.scan(new WifiDiscovery.Callback() {
                @Override public void onResult(List<WifiDiscovery.Server> servers) {
                    runOnUiThread(() -> {
                        scanButton.setEnabled(true);
                        scanButton.setText("扫描同一 Wi-Fi 下的服务端");
                        if (servers.isEmpty()) {
                            Toast.makeText(MainActivity.this,
                                    "没有发现服务端。请确认电脑使用 --lan 启动、在同一 Wi-Fi，且防火墙允许 UDP 8767。",
                                    Toast.LENGTH_LONG).show();
                            return;
                        }
                        String[] labels = new String[servers.size()];
                        for (int i = 0; i < servers.size(); i++) {
                            WifiDiscovery.Server server = servers.get(i);
                            labels[i] = server.name + "\n" + server.host + ":" + server.port
                                    + (server.tokenRequired ? "  (需要 Token)" : "");
                        }
                        AlertDialog found = new AlertDialog.Builder(MainActivity.this)
                                .setTitle("发现的服务端")
                                .setItems(labels, (dialog, which) -> {
                                    WifiDiscovery.Server server = servers.get(which);
                                    hostEdit.setText(server.host);
                                    portEdit.setText(String.valueOf(server.port));
                                    tokenEdit.requestFocus();
                                    Toast.makeText(MainActivity.this,
                                            server.tokenRequired
                                                    ? "已填入地址，请输入电脑端显示的 Token"
                                                    : "已填入地址，点击保存并重连",
                                            Toast.LENGTH_SHORT).show();
                                })
                                .setNegativeButton("取消", null)
                                .create();
                        found.show();
                        styleDialog(found, d);
                    });
                }

                @Override public void onError(String message) {
                    runOnUiThread(() -> {
                        scanButton.setEnabled(true);
                        scanButton.setText("扫描同一 Wi-Fi 下的服务端");
                        Toast.makeText(MainActivity.this, "扫描失败: " + message, Toast.LENGTH_LONG).show();
                    });
                }
            });
        });
        root.addView(scanButton, marginParams(d, 8));

        final float[] sens = {sensitivity};
        TextView sensLabel = new TextView(this);
        sensLabel.setTextColor(0xFFE9EFF8);
        sensLabel.setTextSize(13);
        sensLabel.setText(String.format(Locale.US, "指针速度  %.2f×", sens[0]));
        root.addView(sensLabel, marginParams(d, 8));
        SeekBar sensBar = new SeekBar(this);
        sensBar.setMax(200);
        sensBar.setProgress((int) ((sens[0] - 0.35f) / 2.15f * 200));
        sensBar.setOnSeekBarChangeListener(new SimpleSeekListener() {
            @Override public void onProgressChanged(SeekBar seekBar, int progress, boolean fromUser) {
                sens[0] = 0.35f + progress / 200f * 2.15f;
                sensLabel.setText(String.format(Locale.US, "指针速度  %.2f×", sens[0]));
            }
        });
        root.addView(sensBar, new LinearLayout.LayoutParams(
                LinearLayout.LayoutParams.MATCH_PARENT, LinearLayout.LayoutParams.WRAP_CONTENT));

        final float[] scroll = {scrollSpeed};
        TextView scrollLabel = new TextView(this);
        scrollLabel.setTextColor(0xFFE9EFF8);
        scrollLabel.setTextSize(13);
        scrollLabel.setText(String.format(Locale.US, "滚动速度  %.2f×", scroll[0]));
        root.addView(scrollLabel, marginParams(d, 8));
        SeekBar scrollBar = new SeekBar(this);
        scrollBar.setMax(100);
        scrollBar.setProgress((int) ((scroll[0] - 0.4f) / 1.8f * 100));
        scrollBar.setOnSeekBarChangeListener(new SimpleSeekListener() {
            @Override public void onProgressChanged(SeekBar seekBar, int progress, boolean fromUser) {
                scroll[0] = 0.4f + progress / 100f * 1.8f;
                scrollLabel.setText(String.format(Locale.US, "滚动速度  %.2f×", scroll[0]));
            }
        });
        root.addView(scrollBar, new LinearLayout.LayoutParams(
                LinearLayout.LayoutParams.MATCH_PARENT, LinearLayout.LayoutParams.WRAP_CONTENT));

        Switch hapticSwitch = new Switch(this);
        hapticSwitch.setText("触感反馈");
        hapticSwitch.setTextColor(0xFFE9EFF8);
        hapticSwitch.setTextSize(13);
        hapticSwitch.setChecked(haptics);
        ColorStateList cyan = ColorStateList.valueOf(0xFF63E3FF);
        ColorStateList track = ColorStateList.valueOf(0x5528C9E8);
        hapticSwitch.setThumbTintList(cyan);
        hapticSwitch.setTrackTintList(track);
        root.addView(hapticSwitch, marginParams(d, 10));

        Switch naturalSwitch = new Switch(this);
        naturalSwitch.setText("自然滚动（双指方向与内容一致）");
        naturalSwitch.setTextColor(0xFFE9EFF8);
        naturalSwitch.setTextSize(13);
        naturalSwitch.setChecked(naturalScroll);
        naturalSwitch.setThumbTintList(cyan);
        naturalSwitch.setTrackTintList(track);
        root.addView(naturalSwitch, marginParams(d, 4));

        Switch btSwitch = new Switch(this);
        btSwitch.setText("外接蓝牙键盘转发");
        btSwitch.setTextColor(0xFFE9EFF8);
        btSwitch.setTextSize(13);
        btSwitch.setChecked(btKeyboardEnabled);
        btSwitch.setThumbTintList(cyan);
        btSwitch.setTrackTintList(track);
        root.addView(btSwitch, marginParams(d, 10));

        TextView tip = new TextView(this);
        tip.setText("提示：双击触摸板后不松手即可拖动窗口；长按也可以拖动。三指轻点 = 中键。\n外接蓝牙键盘：先在 Android 设置中配对，开启上面的开关后实体按键会转发到电脑。");
        tip.setTextColor(0xFF66758A);
        tip.setTextSize(11);
        tip.setLineSpacing(0, 1.2f);
        root.addView(tip, marginParams(d, 12));

        ScrollView scrollView = new ScrollView(this);
        scrollView.addView(root);
        AlertDialog dialog = new AlertDialog.Builder(this)
                .setTitle("PadKey 连接设置")
                .setView(scrollView)
                .setPositiveButton("保存并重连", null)
                .setNegativeButton("取消", null)
                .create();
        dialog.setOnShowListener(ignored -> dialog.getButton(AlertDialog.BUTTON_POSITIVE).setOnClickListener(v -> {
            String h = hostEdit.getText().toString().trim();
            if (h.isEmpty()) h = "127.0.0.1";
            int p;
            try {
                p = Integer.parseInt(portEdit.getText().toString().trim());
            } catch (Exception e) {
                p = 8765;
            }
            if (p < 1 || p > 65535) p = 8765;
            host = h;
            port = p;
            token = tokenEdit.getText().toString().trim();
            sensitivity = sens[0];
            scrollSpeed = scroll[0];
            haptics = hapticSwitch.isChecked();
            naturalScroll = naturalSwitch.isChecked();
            btKeyboardEnabled = btSwitch.isChecked();
            if (connection != null) connection.releaseAll();
            saveSettings();
            applySettingsToViews();
            if (connection != null) {
                connection.stop();
                connection.configure(host, port, token);
                connection.start();
            }
            dialog.dismiss();
            Toast.makeText(this, "已保存，正在连接", Toast.LENGTH_SHORT).show();
        }));
        dialog.setOnDismissListener(ignored -> settingsOpen = false);
        settingsOpen = true;
        dialog.show();
        styleDialog(dialog, d);
    }

    private void styleDialog(AlertDialog dialog, float d) {
        android.view.Window window = dialog.getWindow();
        if (window != null) {
            GradientDrawable bg = new GradientDrawable();
            bg.setColor(0xFF0C1219);
            bg.setCornerRadius(20 * d);
            window.setBackgroundDrawable(bg);
            window.setDimAmount(0.62f);
        }
        if (dialog.getButton(AlertDialog.BUTTON_POSITIVE) != null)
            dialog.getButton(AlertDialog.BUTTON_POSITIVE).setTextColor(0xFF63E3FF);
        if (dialog.getButton(AlertDialog.BUTTON_NEGATIVE) != null)
            dialog.getButton(AlertDialog.BUTTON_NEGATIVE).setTextColor(0xFF8794A8);
    }

    private void addField(LinearLayout root, String label, EditText edit, float d) {
        TextView title = new TextView(this);
        title.setText(label);
        title.setTextColor(0xFF9FB0C6);
        title.setTextSize(12);
        title.setPadding(0, (int) (10 * d), 0, 0);
        root.addView(title);
        root.addView(edit, new LinearLayout.LayoutParams(
                LinearLayout.LayoutParams.MATCH_PARENT, LinearLayout.LayoutParams.WRAP_CONTENT));
    }

    private LinearLayout.LayoutParams marginParams(float d, int topDp) {
        LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(
                LinearLayout.LayoutParams.MATCH_PARENT, LinearLayout.LayoutParams.WRAP_CONTENT);
        lp.topMargin = (int) (topDp * d);
        return lp;
    }

    private abstract static class SimpleSeekListener implements SeekBar.OnSeekBarChangeListener {
        @Override public void onStartTrackingTouch(SeekBar seekBar) { }
        @Override public void onStopTrackingTouch(SeekBar seekBar) { }
    }
}
