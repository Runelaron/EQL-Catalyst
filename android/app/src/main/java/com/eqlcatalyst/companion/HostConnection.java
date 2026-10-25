package com.eqlcatalyst.companion;

import android.util.Base64;
import java.io.ByteArrayInputStream;
import java.net.InetAddress;
import java.net.URI;
import java.security.KeyStore;
import java.security.cert.CertificateFactory;
import java.security.cert.X509Certificate;
import java.util.List;
import java.util.concurrent.TimeUnit;
import javax.net.ssl.SSLContext;
import javax.net.ssl.TrustManager;
import javax.net.ssl.TrustManagerFactory;
import javax.net.ssl.X509TrustManager;
import okhttp3.*;
import org.json.JSONObject;

/** QR cert trust is restricted to this connection. Default hostname verification stays enabled. */
final class HostConnection {
    private final OkHttpClient client;
    final String base;
    final String serverId;
    final String certificate;
    String credential;
    private static final MediaType JSON = MediaType.get("application/json; charset=utf-8");
    private HostConnection(OkHttpClient client, String base, String serverId, String certificate, String credential) { this.client = client; this.base = base; this.serverId = serverId; this.certificate = certificate; this.credential = credential; }
    static HostConnection bootstrap(JSONObject qr) throws Exception {
        if (qr.getInt("protocol") != ProtocolRules.VERSION) throw new Exception("Protocol mismatch. Update the Android beta.");
        String id = qr.getString("serverId"), host = ProtocolRules.serverHost(id); URI address = ProtocolRules.address(qr.getString("address"));
        String cert = qr.getString("certificate");
        byte[] der = Base64.decode(cert, Base64.NO_WRAP);
        if (!ProtocolRules.samePin(qr.getString("certificateSha256"), der)) throw new java.security.cert.CertificateException("QR certificate fingerprint mismatch");
        HostConnection connection = trusted(address.toString(), id, cert, null);
        connection.verifyIdentity();
        return connection;
    }
    static HostConnection trusted(String rawAddress, String id, String cert, String credential) throws Exception {
        URI address = ProtocolRules.address(rawAddress); String host = ProtocolRules.serverHost(id);
        X509Certificate anchor = (X509Certificate) CertificateFactory.getInstance("X.509").generateCertificate(new ByteArrayInputStream(Base64.decode(cert, Base64.NO_WRAP))); anchor.checkValidity();
        KeyStore store = KeyStore.getInstance(KeyStore.getDefaultType()); store.load(null); store.setCertificateEntry("paired-pc", anchor);
        TrustManagerFactory factory = TrustManagerFactory.getInstance(TrustManagerFactory.getDefaultAlgorithm()); factory.init(store); X509TrustManager manager = (X509TrustManager) factory.getTrustManagers()[0];
        SSLContext tls = SSLContext.getInstance("TLS"); tls.init(null, new TrustManager[] { manager }, null);
        String pin = CertificatePinner.pin(anchor); OkHttpClient client = builder(address, host).sslSocketFactory(tls.getSocketFactory(), manager).certificatePinner(new CertificatePinner.Builder().add(host, pin).build()).build();
        return new HostConnection(client, "https://" + host + ":" + address.getPort(), id, cert, credential);
    }
    private static OkHttpClient.Builder builder(URI address, String host) {
        return new OkHttpClient.Builder().dns(name -> { if (!name.equals(host)) throw new java.net.UnknownHostException("Unpaired host"); return List.of(InetAddress.getByName(address.getHost())); }).followRedirects(false).followSslRedirects(false).connectTimeout(8, TimeUnit.SECONDS).readTimeout(15, TimeUnit.SECONDS).callTimeout(20, TimeUnit.SECONDS).connectionSpecs(List.of(ConnectionSpec.MODERN_TLS));
    }
    JSONObject request(String path, JSONObject body, boolean authenticate) throws Exception {
        Request.Builder request = new Request.Builder().url(base + path).header("X-Catalyst-Protocol", "1");
        if (authenticate) { if (credential == null) throw new Exception("Pairing required"); request.header("Authorization", "Bearer " + credential); }
        if (body != null) request.post(RequestBody.create(body.toString(), JSON));
        try (Response response = client.newCall(request.build()).execute()) {
            if (response.code() == 426) throw new Exception("Protocol mismatch. Update the Android beta."); if (response.code() == 401) throw new Exception("Device is unpaired or revoked. Verify pairing on the PC.");
            if (!response.isSuccessful() || response.body() == null) throw new Exception("Connection unavailable (" + response.code() + ")");
            if (response.body().contentLength() > 2_000_000) throw new Exception("Host response too large"); String text = response.body().string(); if (text.length() > 2_000_000) throw new Exception("Host response too large"); return text.isEmpty() ? new JSONObject() : new JSONObject(text);
        }
    }
    void verifyIdentity() throws Exception { JSONObject identity = request("/identity", null, false); if (!serverId.equals(identity.optString("serverId")) || identity.optInt("protocol") != 1) throw new Exception("Host identity/protocol changed. Verified re-pairing is required."); }
    WebSocket stream(WebSocketListener listener) { return client.newWebSocket(new Request.Builder().url(base + "/api/v1/stream").header("Authorization", "Bearer " + credential).header("Sec-WebSocket-Protocol", "catalyst.v1").header("X-Catalyst-Protocol", "1").build(), listener); }
    void close() { client.dispatcher().cancelAll(); client.connectionPool().evictAll(); client.dispatcher().executorService().shutdown(); }
}
