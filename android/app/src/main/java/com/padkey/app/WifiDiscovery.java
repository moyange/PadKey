package com.padkey.app;

import org.json.JSONObject;

import java.net.DatagramPacket;
import java.net.DatagramSocket;
import java.net.InetAddress;
import java.net.SocketTimeoutException;
import java.nio.charset.StandardCharsets;
import java.util.ArrayList;
import java.util.List;

/** Finds PadKey servers on the same Wi-Fi/LAN using a UDP broadcast. */
public final class WifiDiscovery {
    public static final int DISCOVERY_PORT = 8767;
    private static final String PROBE = "PADKEY_DISCOVER_V1";

    public interface Callback {
        void onResult(List<Server> servers);
        void onError(String message);
    }

    public static final class Server {
        public final String name;
        public final String host;
        public final int port;
        public final boolean tokenRequired;

        Server(String name, String host, int port, boolean tokenRequired) {
            this.name = name;
            this.host = host;
            this.port = port;
            this.tokenRequired = tokenRequired;
        }
    }

    private WifiDiscovery() { }

    public static void scan(Callback callback) {
        Thread thread = new Thread(() -> {
            List<Server> found = new ArrayList<>();
            DatagramSocket socket = null;
            try {
                socket = new DatagramSocket();
                socket.setBroadcast(true);
                socket.setSoTimeout(450);
                byte[] probe = PROBE.getBytes(StandardCharsets.US_ASCII);
                DatagramPacket request = new DatagramPacket(
                        probe, probe.length,
                        InetAddress.getByName("255.255.255.255"), DISCOVERY_PORT);
                socket.send(request);

                long deadline = System.currentTimeMillis() + 2200;
                while (System.currentTimeMillis() < deadline) {
                    byte[] buffer = new byte[1024];
                    DatagramPacket response = new DatagramPacket(buffer, buffer.length);
                    try {
                        socket.receive(response);
                    } catch (SocketTimeoutException timeout) {
                        continue;
                    }
                    String text = new String(response.getData(), 0, response.getLength(), StandardCharsets.UTF_8);
                    JSONObject o = new JSONObject(text);
                    if (!"padkey-server".equals(o.optString("t"))) continue;
                    String host = response.getAddress().getHostAddress();
                    if (host.startsWith("::ffff:")) host = host.substring(7);
                    String name = o.optString("name", "PC");
                    int port = o.optInt("port", 8765);
                    boolean tokenRequired = o.optBoolean("tokenRequired", false);
                    boolean duplicate = false;
                    for (Server s : found) {
                        if (s.host.equals(host) && s.port == port) { duplicate = true; break; }
                    }
                    if (!duplicate) found.add(new Server(name, host, port, tokenRequired));
                }
            } catch (Exception e) {
                callback.onError(e.getMessage() == null ? e.getClass().getSimpleName() : e.getMessage());
                return;
            } finally {
                if (socket != null) socket.close();
            }
            callback.onResult(found);
        }, "padkey-wifi-scan");
        thread.setDaemon(true);
        thread.start();
    }
}
