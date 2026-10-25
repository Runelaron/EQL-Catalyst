package com.eqlcatalyst.companion;

import java.net.URI;
import java.security.MessageDigest;
import java.util.Locale;

final class ProtocolRules {
    static final int VERSION = 1;
    static URI address(String raw) {
        URI uri = URI.create(raw);
        if (!"https".equals(uri.getScheme()) || uri.getUserInfo() != null || uri.getRawQuery() != null || uri.getFragment() != null || uri.getHost() == null || uri.getPort() < 1024 || uri.getPort() > 65535 || uri.getPath() != null && !uri.getPath().isEmpty() && !uri.getPath().equals("/")) throw new IllegalArgumentException("Use the HTTPS LAN address from PC settings");
        String[] cells = uri.getHost().split("\\."); if (cells.length != 4) throw new IllegalArgumentException("Use the private IPv4 address from PC pairing");
        int[] n = new int[4]; for (int i = 0; i < 4; i++) { n[i] = Integer.parseInt(cells[i]); if (n[i] < 0 || n[i] > 255) throw new IllegalArgumentException("Invalid address"); }
        if (!(n[0] == 10 || n[0] == 172 && n[1] >= 16 && n[1] <= 31 || n[0] == 192 && n[1] == 168)) throw new IllegalArgumentException("Only private LAN hosts are supported"); return uri;
    }
    static String hash(byte[] bytes) throws Exception { StringBuilder result = new StringBuilder(); for (byte b : MessageDigest.getInstance("SHA-256").digest(bytes)) result.append(String.format(Locale.ROOT, "%02x", b & 255)); return result.toString(); }
    static boolean samePin(String expected, byte[] certificate) throws Exception { return expected.matches("[0-9a-f]{64}") && MessageDigest.isEqual(expected.getBytes(java.nio.charset.StandardCharsets.US_ASCII), hash(certificate).getBytes(java.nio.charset.StandardCharsets.US_ASCII)); }
    static String serverHost(String id) { if (!id.matches("[0-9a-f]{32}")) throw new IllegalArgumentException("Invalid server identity"); return "catalyst-" + id + ".local"; }
    static boolean updateMatches(long revision, long base) { return revision == base; }
}
