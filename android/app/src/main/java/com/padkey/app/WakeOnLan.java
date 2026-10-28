package com.padkey.app;

import java.net.DatagramPacket;
import java.net.DatagramSocket;
import java.net.InetAddress;
import java.util.ArrayList;
import java.util.List;

/**
 * Wake-on-LAN is disabled for fake sleep: the PC stays running, so no magic packet is needed.
 * Kept for a future real-sleep mode.
 */
public final class WakeOnLan {
    private WakeOnLan() { }

    public static int wake(String host, String[] macs) {
        List<byte[]> targets = new ArrayList<>();
        if (macs != null) {
            for (String mac : macs) {
                byte[] packet = packet(mac);
                if (packet != null) targets.add(packet);
            }
        }
        if (targets.isEmpty()) return 0;

        int sent = 0;
        try (DatagramSocket socket = new DatagramSocket()) {
            socket.setBroadcast(true);
            List<InetAddress> addresses = new ArrayList<>();
            addresses.add(InetAddress.getByName("255.255.255.255"));
            if (host != null && host.matches("^\\d{1,3}(\\.\\d{1,3}){3}$") && !host.startsWith("127.")) {
                int dot = host.lastIndexOf('.');
                addresses.add(InetAddress.getByName(host.substring(0, dot + 1) + "255"));
            }
            for (byte[] packet : targets) {
                for (InetAddress address : addresses) {
                    for (int port : new int[]{9, 7}) {
                        try {
                            socket.send(new DatagramPacket(packet, packet.length, address, port));
                            sent++;
                        } catch (Exception ignored) { }
                    }
                }
            }
        } catch (Exception ignored) { }
        return sent;
    }

    private static byte[] packet(String mac) {
        if (mac == null) return null;
        String[] parts = mac.trim().split("[:-]");
        if (parts.length != 6) return null;
        byte[] address = new byte[6];
        try {
            for (int i = 0; i < 6; i++) address[i] = (byte) Integer.parseInt(parts[i], 16);
        } catch (NumberFormatException e) {
            return null;
        }
        byte[] packet = new byte[6 + 16 * 6];
        for (int i = 0; i < 6; i++) packet[i] = (byte) 0xFF;
        for (int i = 0; i < 16; i++) {
            System.arraycopy(address, 0, packet, 6 + i * 6, 6);
        }
        return packet;
    }
}
