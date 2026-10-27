package com.padkey.app;

import android.os.Build;
import android.os.Handler;
import android.os.Looper;
import android.os.SystemClock;
import android.util.Base64;
import android.util.Log;

import org.json.JSONArray;
import org.json.JSONObject;

import java.io.BufferedInputStream;
import java.io.ByteArrayOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.net.InetSocketAddress;
import java.net.Socket;
import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.security.SecureRandom;
import java.util.Locale;
import java.util.concurrent.LinkedBlockingQueue;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.atomic.AtomicBoolean;

/**
 * Minimal RFC6455 WebSocket client. Keeping this dependency-free makes the APK tiny
 * and lets the app run on any Android 8+ tablet without support libraries.
 */
public final class ConnectionManager {
    private static final String TAG = "PadKeyConn";
    private static final String WS_GUID = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";

    public enum State { DISCONNECTED, CONNECTING, CONNECTED }

    public interface Listener {
        void onState(State state, String detail);
        void onRtt(long rttMs);
        void onWelcome(String pc, String link);
        void onLocked(boolean locked);
        void onWakeInfo(String[] macs);
        void onPowerResult(boolean ok, String message);
    }

    private final Handler main = new Handler(Looper.getMainLooper());
    private final Listener listener;
    private final AtomicBoolean running = new AtomicBoolean(false);
    private final LinkedBlockingQueue<String> outbox = new LinkedBlockingQueue<>(1024);
    private final Object socketLock = new Object();
    private final Object writeLock = new Object();
    private final SecureRandom random = new SecureRandom();

    private volatile String host = "127.0.0.1";
    private volatile int port = 8765;
    private volatile String token = "";
    private volatile Socket socket;
    private volatile BufferedInputStream input;
    private volatile OutputStream output;
    private volatile boolean connected;
    private volatile boolean pingOutstanding;
    private volatile long pingSentAt;
    private volatile long lastRtt = -1;
    private volatile long lastPongAt;

    private Thread connectThread;
    private Thread writerThread;
    private Thread pingThread;

    public ConnectionManager(Listener listener) {
        this.listener = listener;
    }

    public void configure(String host, int port, String token) {
        this.host = host == null || host.trim().isEmpty() ? "127.0.0.1" : host.trim();
        this.port = port < 1 || port > 65535 ? 8765 : port;
        this.token = token == null ? "" : token.trim();
    }

    public String getHost() { return host; }
    public int getPort() { return port; }
    public String getToken() { return token; }
    public boolean isConnected() { return connected; }
    public long getLastRtt() { return lastRtt; }

    public void start() {
        if (!running.compareAndSet(false, true)) return;
        connectThread = new Thread(this::connectLoop, "padkey-connect");
        connectThread.setDaemon(true);
        connectThread.start();
    }

    public void stop() {
        running.set(false);
        closeSocket();
        if (connectThread != null) {
            try { connectThread.join(200); } catch (InterruptedException ignored) { }
        }
        connectThread = null;
        writerThread = null;
        pingThread = null;
    }

    private void connectLoop() {
        int attempt = 0;
        while (running.get()) {
            if (attempt == 0) postState(State.CONNECTING, host + ":" + port);
            try {
                connectOnce();
            } catch (IOException e) {
                if (running.get()) postState(State.DISCONNECTED, shortReason(e));
            } catch (Exception e) {
                if (running.get()) postState(State.DISCONNECTED, shortReason(e));
            } finally {
                closeSocket();
            }

            if (!running.get()) break;
            attempt++;
            try { Thread.sleep(1100); } catch (InterruptedException e) { break; }
        }
        postState(State.DISCONNECTED, "");
    }

    private static String shortReason(Exception e) {
        String msg = e.getMessage();
        if (msg == null || msg.isEmpty()) msg = e.getClass().getSimpleName();
        return msg.length() > 64 ? msg.substring(0, 64) : msg;
    }

    private void connectOnce() throws Exception {
        Socket s = new Socket();
        synchronized (socketLock) {
            if (!running.get()) {
                s.close();
                return;
            }
            socket = s;
        }
        s.connect(new InetSocketAddress(host, port), 2500);
        s.setTcpNoDelay(true);
        s.setKeepAlive(true);
        s.setSoTimeout(3000);

        BufferedInputStream in = new BufferedInputStream(s.getInputStream(), 8192);
        OutputStream out = s.getOutputStream();

        String key = generateKey();
        String request = "GET / HTTP/1.1\r\n"
                + "Host: " + host + ":" + port + "\r\n"
                + "Upgrade: websocket\r\n"
                + "Connection: Upgrade\r\n"
                + "Sec-WebSocket-Key: " + key + "\r\n"
                + "Sec-WebSocket-Version: 13\r\n"
                + "\r\n";
        out.write(request.getBytes(StandardCharsets.US_ASCII));
        out.flush();

        String header = readHeader(in);
        if (header == null) throw new IOException("连接被关闭");
        String first = header.split("\r\n", 2)[0];
        if (!first.contains(" 101")) throw new IOException("握手失败: " + first.trim());

        String expected = Base64.encodeToString(
                MessageDigest.getInstance("SHA-1").digest((key + WS_GUID).getBytes(StandardCharsets.US_ASCII)),
                Base64.NO_WRAP);
        if (!header.toLowerCase(Locale.US).contains("sec-websocket-accept: " + expected.toLowerCase(Locale.US))) {
            throw new IOException("WebSocket 校验失败");
        }

        s.setSoTimeout(0);
        synchronized (socketLock) {
            if (!running.get() || s.isClosed()) {
                s.close();
                return;
            }
            input = in;
            output = out;
            connected = true;
            lastPongAt = SystemClock.elapsedRealtime();
            outbox.clear();
        }

        sendFrame(0x1, buildHello().getBytes(StandardCharsets.UTF_8));
        postState(State.CONNECTED, host);
        startWriter();
        startPing();
        readLoop();
    }

    private String buildHello() {
        String name;
        try {
            name = (Build.MANUFACTURER + " " + Build.MODEL).trim();
        } catch (Exception e) {
            name = "Android Pad";
        }
        try {
            JSONObject o = new JSONObject();
            o.put("t", "hello");
            o.put("v", 1);
            o.put("token", token);
            o.put("name", name);
            return o.toString();
        } catch (Exception e) {
            return "{\"t\":\"hello\",\"v\":1,\"token\":\"\",\"name\":\"Android Pad\"}";
        }
    }

    private void startWriter() {
        writerThread = new Thread(() -> {
            try {
                while (running.get() && connected) {
                    String msg = outbox.poll(500, TimeUnit.MILLISECONDS);
                    if (msg == null) continue;
                    sendFrame(0x1, msg.getBytes(StandardCharsets.UTF_8));
                }
            } catch (Exception e) {
                if (running.get()) Log.d(TAG, "writer stopped: " + e.getMessage());
                closeSocket();
            }
        }, "padkey-writer");
        writerThread.setDaemon(true);
        writerThread.start();
    }

    private void startPing() {
        pingThread = new Thread(() -> {
            try {
                while (running.get() && connected) {
                    Thread.sleep(2000);
                    if (!connected) break;
                    long now = SystemClock.elapsedRealtime();
                    if (now - lastPongAt > 7000) {
                        Log.w(TAG, "heartbeat timeout; reconnecting");
                        closeSocket();
                        break;
                    }
                    pingSentAt = now;
                    pingOutstanding = true;
                    String msg = "{\"t\":\"ping\",\"ts\":" + pingSentAt + "}";
                    if (!outbox.offer(msg)) {
                        // The queue is saturated by movement; bypass it for the heartbeat.
                        sendFrame(0x1, msg.getBytes(StandardCharsets.UTF_8));
                    }
                }
            } catch (InterruptedException ignored) {
            } catch (Exception e) {
                closeSocket();
            }
        }, "padkey-ping");
        pingThread.setDaemon(true);
        pingThread.start();
    }

    private void readLoop() {
        try {
            while (running.get() && connected) {
                Frame frame = readFrame();
                if (frame == null) break;
                if (frame.opcode == 0x8) {
                    break;
                } else if (frame.opcode == 0xA) {
                    handlePong(frame.payload);
                } else if (frame.opcode == 0x1) {
                    handleText(new String(frame.payload, StandardCharsets.UTF_8));
                } else if (frame.opcode == 0x9) {
                    sendFrame(0xA, frame.payload);
                }
            }
        } catch (IOException e) {
            if (running.get()) Log.d(TAG, "read ended: " + e.getMessage());
        } finally {
            connected = false;
            closeSocket();
            postState(State.DISCONNECTED, "");
        }
    }

    private void handleText(String text) {
        try {
            JSONObject o = new JSONObject(text);
            String type = o.optString("t");
            if ("welcome".equals(type)) {
                String pc = o.optString("pc", "PC");
                String link = o.optString("link", "USB");
                main.post(() -> listener.onWelcome(pc, link));
            } else if ("pong".equals(type)) {
                long ts = o.optLong("ts", 0);
                if (ts > 0) {
                    long now = SystemClock.elapsedRealtime();
                    lastPongAt = now;
                    long rtt = Math.max(0, now - ts);
                    lastRtt = rtt;
                    pingOutstanding = false;
                    main.post(() -> listener.onRtt(rtt));
                }
            } else if ("locked".equals(type)) {
                boolean locked = o.optBoolean("locked", false);
                main.post(() -> listener.onLocked(locked));
            } else if ("wakeInfo".equals(type)) {
                JSONArray arr = o.optJSONArray("macs");
                String[] macs = new String[arr == null ? 0 : arr.length()];
                for (int i = 0; i < macs.length; i++) macs[i] = arr.optString(i, "");
                main.post(() -> listener.onWakeInfo(macs));
            } else if ("power".equals(type)) {
                boolean ok = o.optBoolean("ok", false);
                String message = o.optString("message", "");
                main.post(() -> listener.onPowerResult(ok, message));
            } else if ("error".equals(type)) {
                Log.w(TAG, "server: " + o.optString("message"));
            }
        } catch (Exception e) {
            Log.w(TAG, "bad message: " + text);
        }
    }

    private void handlePong(byte[] payload) {
        try {
            if (payload.length == 0) return;
            JSONObject o = new JSONObject(new String(payload, StandardCharsets.UTF_8));
            if ("pong".equals(o.optString("t"))) {
                long ts = o.optLong("ts", 0);
                if (ts > 0) {
                    long now = SystemClock.elapsedRealtime();
                    lastPongAt = now;
                    long rtt = Math.max(0, now - ts);
                    lastRtt = rtt;
                    pingOutstanding = false;
                    main.post(() -> listener.onRtt(rtt));
                }
            }
        } catch (Exception ignored) { }
    }

    private void postState(State state, String detail) {
        main.post(() -> listener.onState(state, detail));
    }

    // ---- public event senders -------------------------------------------------

    public void sendPower(String action) {
        enqueue("{\"t\":\"power\",\"action\":\"" + action + "\"}", true);
    }

    public void sendKey(int hid, boolean down) {
        enqueue("{\"t\":\"key\",\"code\":" + hid + ",\"down\":" + down + "}", true);
    }

    public void sendMove(float dx, float dy) {
        if (!connected) return;
        enqueue(String.format(Locale.US, "{\"t\":\"move\",\"dx\":%.3f,\"dy\":%.3f}", dx, dy), false);
    }

    public void sendButton(String button, boolean down) {
        enqueue("{\"t\":\"button\",\"b\":\"" + button + "\",\"down\":" + down + "}", true);
    }

    public void sendScroll(int dx, int dy) {
        enqueue("{\"t\":\"scroll\",\"dx\":" + dx + ",\"dy\":" + dy + "}", false);
    }

    public void releaseAll() {
        String msg = "{\"t\":\"release\"}";
        if (connected) {
            try { sendFrame(0x1, msg.getBytes(StandardCharsets.UTF_8)); } catch (Exception ignored) { }
        }
        outbox.clear();
    }

    private void enqueue(String json, boolean critical) {
        if (!connected) return;
        if (critical) {
            outbox.offer(json);
        } else if (outbox.size() < 256) {
            outbox.offer(json);
        }
    }

    // ---- low level ------------------------------------------------------------

    private void closeSocket() {
        connected = false;
        pingOutstanding = false;
        outbox.clear();
        synchronized (socketLock) {
            Socket s = socket;
            socket = null;
            input = null;
            output = null;
            if (s != null) {
                try { s.close(); } catch (IOException ignored) { }
            }
        }
    }

    private void sendFrame(int opcode, byte[] payload) throws IOException {
        synchronized (writeLock) {
            OutputStream out = output;
            if (out == null) return;
            int len = payload.length;
            ByteArrayOutputStream header = new ByteArrayOutputStream(14);
            header.write(0x80 | opcode);
            if (len <= 125) {
                header.write(0x80 | len);
            } else if (len <= 65535) {
                header.write(0x80 | 126);
                header.write((len >>> 8) & 0xFF);
                header.write(len & 0xFF);
            } else {
                header.write(0x80 | 127);
                for (int i = 7; i >= 0; i--) header.write((int) (((long) len >>> (8 * i)) & 0xFF));
            }
            byte[] mask = new byte[4];
            random.nextBytes(mask);
            header.write(mask, 0, 4);
            out.write(header.toByteArray());
            byte[] masked = new byte[len];
            for (int i = 0; i < len; i++) masked[i] = (byte) (payload[i] ^ mask[i & 3]);
            out.write(masked);
            out.flush();
        }
    }

    private Frame readFrame() throws IOException {
        InputStream in = input;
        if (in == null) return null;
        int b0 = in.read();
        if (b0 < 0) return null;
        int b1 = in.read();
        if (b1 < 0) return null;

        int opcode = b0 & 0x0F;
        long len = b1 & 0x7F;
        if (len == 126) {
            len = ((long) (in.read() & 0xFF) << 8) | (in.read() & 0xFF);
        } else if (len == 127) {
            len = 0;
            for (int i = 0; i < 8; i++) len = (len << 8) | (in.read() & 0xFF);
        }
        if (len < 0 || len > 1024 * 1024) throw new IOException("frame too large");

        boolean masked = (b1 & 0x80) != 0;
        byte[] mask = null;
        if (masked) {
            mask = new byte[4];
            readFully(in, mask, 0, 4);
        }
        byte[] payload = new byte[(int) len];
        readFully(in, payload, 0, payload.length);
        if (masked && mask != null) {
            for (int i = 0; i < payload.length; i++) payload[i] = (byte) (payload[i] ^ mask[i & 3]);
        }
        return new Frame(opcode, payload);
    }

    private static void readFully(InputStream in, byte[] buffer, int off, int len) throws IOException {
        int done = 0;
        while (done < len) {
            int n = in.read(buffer, off + done, len - done);
            if (n < 0) throw new IOException("连接已断开");
            done += n;
        }
    }

    private static String readHeader(InputStream in) throws IOException {
        ByteArrayOutputStream out = new ByteArrayOutputStream(512);
        int state = 0;
        while (out.size() < 16384) {
            int b = in.read();
            if (b < 0) return null;
            out.write(b);
            if ((state == 0 || state == 2) && b == '\r') state++;
            else if ((state == 1 || state == 3) && b == '\n') state++;
            else state = b == '\r' ? 1 : 0;
            if (state == 4) return out.toString("US-ASCII");
        }
        return null;
    }

    private static String generateKey() {
        byte[] bytes = new byte[16];
        new SecureRandom().nextBytes(bytes);
        return Base64.encodeToString(bytes, Base64.NO_WRAP);
    }

    private static final class Frame {
        final int opcode;
        final byte[] payload;
        Frame(int opcode, byte[] payload) {
            this.opcode = opcode;
            this.payload = payload;
        }
    }
}
