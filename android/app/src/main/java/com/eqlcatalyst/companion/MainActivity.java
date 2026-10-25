package com.eqlcatalyst.companion;

import android.app.Activity;
import android.content.Intent;
import android.graphics.Color;
import android.net.nsd.NsdManager;
import android.net.nsd.NsdServiceInfo;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.view.View;
import android.view.WindowInsets;
import android.widget.*;
import com.google.zxing.integration.android.IntentIntegrator;
import com.google.zxing.integration.android.IntentResult;
import java.net.URLEncoder;
import java.nio.charset.StandardCharsets;
import java.util.HashSet;
import java.util.Set;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import okhttp3.Response;
import okhttp3.WebSocket;
import okhttp3.WebSocketListener;
import org.json.JSONArray;
import org.json.JSONObject;

public final class MainActivity extends Activity {
    private final Handler ui = new Handler(Looper.getMainLooper());
    private final ExecutorService network = Executors.newSingleThreadExecutor();
    private SecretStore secrets;
    private HostConnection connection;
    private WebSocket stream;
    private JSONObject saved, snapshot;
    private LinearLayout root, body, content, discovered, pairing, lookupResults;
    private TextView status, context, freshness;
    private ScrollView scroll;
    private final int[] scrollPositions = new int[3];
    private final Set<String> expanded = new HashSet<>();
    private final String[] destinations = {"build", "goals", "session"};
    private int destination = 0, backoff = 1000;
    private long revision = -1;
    private volatile boolean destroyed, pairingInProgress, protocolMismatch;
    private boolean connected;
    private volatile int connectionGeneration;
    private int renderedDestination = -1;
    private final java.util.Map<String,java.util.List<JSONObject>> historyRows = new java.util.HashMap<>();
    private final java.util.Map<String,Integer> historyOffsets = new java.util.HashMap<>();
    private NsdManager nsd;
    private NsdManager.DiscoveryListener discovery;
    private final Runnable reconnect = this::connect;

    @Override public void onCreate(Bundle state) {
        super.onCreate(state); secrets = new SecretStore(this);
        root = column(); root.setBackgroundColor(Color.parseColor("#081621")); setContentView(root);fitWidth();root.setOnApplyWindowInsetsListener((view,insets)->{root.setPadding(dp(16)+insets.getSystemWindowInsetLeft(),dp(8)+insets.getSystemWindowInsetTop(),dp(16)+insets.getSystemWindowInsetRight(),dp(4)+insets.getSystemWindowInsetBottom());return insets;});root.requestApplyInsets();
        TextView brand = text("Catalyst", 25, "#E4C777");brand.setTypeface(android.graphics.Typeface.SERIF);root.addView(brand); root.addView(text("Companion beta · read-only", 11, "#C7E4E9"));
        context = text("Pair with your PC", 14, "#E8F8FC"); status = text("Disconnected", 12, "#7BE8C5"); root.addView(context); root.addView(status);
        LinearLayout nav = new LinearLayout(this); Button[] tabs = new Button[3];
        for (int i = 0; i < 3; i++) { final int selected = i; Button tab = button(new String[] {"Build", "Goals", "Session"}[i], () -> { scrollPositions[destination] = scroll.getScrollY(); destination = selected; for (int n = 0; n < tabs.length; n++) tabs[n].setSelected(n == selected);render(); });tab.setSelected(i==0);tabs[i] = tab; nav.addView(tab, new LinearLayout.LayoutParams(0, -2, 1)); }
        root.addView(nav); scroll = new ScrollView(this); scroll.setFillViewport(true); body = column(); scroll.addView(body); root.addView(scroll, new LinearLayout.LayoutParams(-1, 0, 1));
        content = column(); body.addView(content); pairing = column(); body.addView(pairing); discovered = column(); body.addView(discovered);
        freshness = text("No snapshot received", 11, "#C7E4E9"); root.addView(freshness);
        root.addView(button("Connection", this::pairingControls));
        LinearLayout lookup = column(); EditText query = new EditText(this); query.setHint("Find a shared item or goal"); query.setMaxLines(1); lookup.addView(query); LinearLayout results = lookupResults = column();
        lookup.addView(button("Find in shared details", () -> { if (connection == null || !connected) { showStatus("Connect to the PC before searching"); return; } String term = query.getText().toString().trim(); if (term.length() > 100) return; network.execute(() -> { try { JSONObject response = connection.request("/api/v1/search?q=" + URLEncoder.encode(term, "UTF-8") + "&offset=0", null, true); ui.post(() -> { if(!connected||snapshot==null||response.optLong("revision")!=revision)return;results.removeAllViews(); JSONArray rows = response.optJSONArray("rows"); if (rows != null) for (int i = 0; i < rows.length(); i++) { JSONObject value = rows.optJSONObject(i); if (value != null) { results.addView(text(value.optString("label"),16,"#E8F8FC")); results.addView(text(value.optString("value"),14,"#7BE8C5")); results.addView(text(value.optString("detail"),12,"#C7E4E9")); } } }); } catch (Exception error) { showStatus("Shared lookup unavailable"); } }); })); lookup.addView(results); body.addView(lookup);
        if (BuildConfig.DEBUG && getIntent().getBooleanExtra("render_fixture", false)) {
            network.execute(() -> { try (java.io.InputStream file = getAssets().open("review-snapshot.json")) { java.io.ByteArrayOutputStream bytes = new java.io.ByteArrayOutputStream(); byte[] buffer = new byte[4096]; int count; while((count=file.read(buffer))!=-1)bytes.write(buffer,0,count); JSONObject fixture = new JSONObject(bytes.toString("UTF-8")); ui.post(() -> { snapshot = fixture; revision = fixture.optLong("revision"); render(); status.setText("Offline synthetic component preview"); }); } catch (Exception error) { showStatus("Review fixture unavailable"); } }); return;
        }
        if(BuildConfig.DEBUG&&getIntent().getBooleanExtra("pair_fixture",false)){network.execute(()->{try{String details=new String(java.nio.file.Files.readAllBytes(new java.io.File(getFilesDir(),"fixture-pair.json").toPath()),StandardCharsets.UTF_8);ui.post(()->pair(details));}catch(Exception error){showStatus("Test pairing file unavailable");}});return;}
        network.execute(() -> {
            try { String record = secrets.read(); if (record != null) saved = new JSONObject(record); ui.post(() -> { if (saved != null) connect(); else pairingControls(); }); }
            catch (Exception e) { showStatus("Protected connection unavailable. Scan a new PC pairing QR."); ui.post(this::pairingControls); }
        });
    }
    private LinearLayout column() { LinearLayout panel = new LinearLayout(this); panel.setOrientation(LinearLayout.VERTICAL); return panel; }
    private int dp(int value){return Math.round(value*getResources().getDisplayMetrics().density);}
    private void fitWidth(){root.setLayoutParams(new android.widget.FrameLayout.LayoutParams(Math.min(getResources().getDisplayMetrics().widthPixels,dp(720)),-1,android.view.Gravity.CENTER_HORIZONTAL));}
    @Override public void onConfigurationChanged(android.content.res.Configuration configuration){super.onConfigurationChanged(configuration);fitWidth();}
    private String theme(String name,String fallback){return snapshot==null||snapshot.optJSONObject("theme")==null?fallback:snapshot.optJSONObject("theme").optString(name,fallback);}
    private TextView text(String value, int size, String color) { TextView label = new TextView(this); label.setText(value); label.setTextSize(size);String key=color.equals("#E4C777")?"accent":color.equals("#7BE8C5")?"mint":color.equals("#C7E4E9")?"secondary":"text";label.setTag(key);label.setTextColor(Color.parseColor(theme(key,color))); label.setPadding(0,dp(3),0,dp(3)); label.setTextIsSelectable(true); return label; }
    private Button button(String label, Runnable action) { Button button = new Button(this); button.setText(label); button.setTextSize(13);button.setAllCaps(false);button.setTag("accent");button.setMinWidth(0);button.setMinHeight(dp(44)); button.setTextColor(Color.parseColor(theme("accent","#69E1F2")));android.graphics.drawable.GradientDrawable shape=new android.graphics.drawable.GradientDrawable();shape.setColor(Color.parseColor(theme("surface","#0c1c29")));shape.setCornerRadius(dp(4));shape.setStroke(dp(1),Color.parseColor(theme("line","#38585d")));button.setBackground(shape);button.setPadding(dp(9),dp(3),dp(9),dp(3));LinearLayout.LayoutParams space=new LinearLayout.LayoutParams(-2,-2);space.setMargins(0,dp(3),dp(5),dp(3));button.setLayoutParams(space);button.setOnClickListener(v -> action.run()); return button; }
    private void showStatus(String value) { ui.post(() -> { if (!destroyed) status.setText(value); }); }
    private void pairingControls() {
        pairing.removeAllViews(); pairing.setVisibility(View.VISIBLE); pairing.addView(text("Connect locally", 20, "#E4C777"));
        pairing.addView(text("In Catalyst on your PC, open Settings → Phone → Pair Android. Scan its QR, then approve this device on the PC.", 14, "#C7E4E9"));
        pairing.addView(button("Scan PC pairing QR", () -> { IntentIntegrator scanner = new IntentIntegrator(this); scanner.setDesiredBarcodeFormats(IntentIntegrator.QR_CODE); scanner.setPrompt("Scan the Catalyst Android pairing QR"); scanner.setBeepEnabled(false); scanner.setOrientationLocked(false); scanner.initiateScan(); }));
        pairing.addView(button("Discover PCs",this::discover));
        EditText manual = new EditText(this); manual.setHint("Manual pairing details from PC (JSON)"); manual.setMaxLines(5); manual.setInputType(android.text.InputType.TYPE_CLASS_TEXT | android.text.InputType.TYPE_TEXT_FLAG_MULTI_LINE); pairing.addView(manual);
        pairing.addView(button("Use pairing details", () -> pair(manual.getText().toString())));
        if (saved != null) {
            EditText address = new EditText(this); address.setSingleLine(true); address.setText(saved.optString("address")); pairing.addView(address);
            pairing.addView(button("Reconnect to manual LAN address", () -> {
                String raw = address.getText().toString().trim(); network.execute(() -> { try { HostConnection candidate = HostConnection.trusted(raw, saved.getString("serverId"), saved.getString("certificate"), saved.getString("credential")); candidate.verifyIdentity(); candidate.close(); saved.put("address", raw); secrets.save(saved.toString()); ui.post(this::connect); } catch (Exception e) { showStatus("Identity/certificate verification failed. Re-pair on your PC."); } });
            }));
            pairing.addView(button("Unpair this phone", () -> { ++connectionGeneration;connected=false;snapshot=null;content.removeAllViews();lookupResults.removeAllViews();historyRows.clear();historyOffsets.clear();freshness.setText("No snapshot received");ui.removeCallbacks(reconnect); if (stream != null) stream.cancel(); network.execute(() -> { try { if (connection != null) connection.request("/api/v1/logout", new JSONObject(), true); } catch (Exception ignored) { } secrets.clear(); saved = null; ui.post(() -> { context.setText("Pair with your PC"); status.setText("Unpaired"); pairingControls(); }); }); }));
        }
        pairing.addView(button("Open-source notices",()->network.execute(()->{try(java.io.InputStream file=getAssets().open("third-party-notices.txt")){java.io.ByteArrayOutputStream bytes=new java.io.ByteArrayOutputStream();byte[] buffer=new byte[4096];int count;while((count=file.read(buffer))!=-1)bytes.write(buffer,0,count);String notices=bytes.toString("UTF-8");ui.post(()->{TextView label=text(notices,12,"#C7E4E9");pairing.addView(label);label.requestFocus();});}catch(Exception error){showStatus("Notices unavailable");}})));
    }
    @Override protected void onActivityResult(int request, int result, Intent data) {
        IntentResult scan = IntentIntegrator.parseActivityResult(request, result, data);
        if (scan != null) { if (scan.getContents() != null) pair(scan.getContents()); return; } super.onActivityResult(request, result, data);
    }
    private void pair(String details) {
        if (pairingInProgress || details.length() > 8192) return; protocolMismatch=false;pairingInProgress = true; showStatus("Verifying the PC certificate…");
        network.execute(() -> {
            HostConnection candidate = null;
            try {
                JSONObject qr = new JSONObject(details); candidate = HostConnection.bootstrap(qr);
                JSONObject request = new JSONObject().put("challenge", qr.getString("challenge")).put("name", android.os.Build.MODEL).put("kind", "android").put("protocol", 1);
                JSONObject ticket = candidate.request("/pair/request", request, false); long until = System.currentTimeMillis() + 120000;
                while (!destroyed && System.currentTimeMillis() < until) {
                    JSONObject outcome = candidate.request("/pair/poll", ticket, false); String result = outcome.getString("status"); showStatus(result+" · code "+ticket.getString("requestId").substring(0,6).toUpperCase(java.util.Locale.ROOT));
                    if ("Expired".equals(result)) throw new Exception("Pairing expired. Generate a new QR on the PC.");
                    if ("Approved".equals(result)) {
                        saved = new JSONObject().put("address", qr.getString("address")).put("serverId", candidate.serverId).put("certificate", candidate.certificate).put("credential", outcome.getString("credential")); secrets.save(saved.toString());
                        pairingInProgress = false; ui.post(() -> { pairing.removeAllViews(); pairing.setVisibility(View.GONE); connect(); }); return;
                    }
                    Thread.sleep(1500);
                }
                throw new Exception("Pairing timed out. Request another QR.");
            } catch (Exception e) { String message=e.getMessage();showStatus(message!=null&&(message.startsWith("Protocol mismatch")||message.startsWith("Pairing expired")||message.startsWith("Pairing timed out"))?message:"Pairing could not be verified. Generate a fresh PC QR and approve the matching code."); }
            finally { if (candidate != null) candidate.close(); pairingInProgress = false; }
        });
    }
    private void connect() {
        if (saved == null || destroyed || pairingInProgress) return; ui.removeCallbacks(reconnect);
        final int attempt = ++connectionGeneration;
        network.execute(() -> {
            try {
                if (stream != null) { stream.cancel(); stream = null; } if (connection != null) connection.close();
                connection = HostConnection.trusted(saved.getString("address"), saved.getString("serverId"), saved.getString("certificate"), saved.getString("credential"));
                connection.verifyIdentity(); JSONObject initial = connection.request("/api/v1/snapshot", null, true); if(attempt!=connectionGeneration)return;accept(initial);
                stream = connection.stream(new WebSocketListener() {
                    @Override public void onOpen(WebSocket socket, Response response) { if(attempt!=connectionGeneration)return;backoff = 1000; connected = true; }
                    @Override public void onMessage(WebSocket socket, String message) { if(attempt!=connectionGeneration)return;if (message.length() > 2_000_000) { socket.cancel(); return; } try { accept(new JSONObject(message)); } catch (Exception e) { showStatus(protocolMismatch?"Protocol mismatch. Update the beta.":"Invalid host response"); socket.cancel(); } }
                    @Override public void onFailure(WebSocket socket, Throwable error, Response response) { if(attempt==connectionGeneration)disconnected(); }
                    @Override public void onClosed(WebSocket socket, int code, String reason) { if(attempt==connectionGeneration)disconnected(); }
                });
            } catch (Exception e) { disconnected(); if(!protocolMismatch)showStatus("Secure connection unavailable. Verify the PC and certificate; re-pair if its identity changed."); }
        });
    }
    private void disconnected() {
        if(protocolMismatch){connected=false;ui.post(()->{ui.removeCallbacks(reconnect);status.setText("Protocol mismatch. Update the beta.");});return;}
        connected = false; showStatus("Disconnected · last received snapshot retained");
        ui.post(() -> { ui.removeCallbacks(reconnect); if (!destroyed && saved != null) { ui.postDelayed(reconnect, backoff); backoff = Math.min(backoff * 2, 30000); } });
    }
    private synchronized void accept(JSONObject envelope) throws Exception {
        if (envelope.getInt("protocol") != 1) { protocolMismatch=true;ui.post(() -> { ui.removeCallbacks(reconnect); status.setText("Protocol mismatch. Update the beta."); }); throw new Exception("Protocol mismatch"); }
        String kind = envelope.getString("type");
        if ("paused".equals(kind)) { snapshot = null;historyRows.clear();historyOffsets.clear();ui.post(() -> { content.removeAllViews();lookupResults.removeAllViews();freshness.setText("Sharing paused");context.setText("Sharing paused"); status.setText("Selected character is not authorized"); }); return; }
        if ("snapshot".equals(kind)){JSONObject next=envelope.getJSONObject("data");if(snapshot==null||!snapshot.optString("contextLabel").equals(next.optString("contextLabel"))){historyRows.clear();historyOffsets.clear();ui.post(()->lookupResults.removeAllViews());}snapshot=next;}
        else if ("update".equals(kind)) {
            if (snapshot == null || !ProtocolRules.updateMatches(revision, envelope.getLong("baseRevision"))) { network.execute(() -> { try { accept(connection.request("/api/v1/snapshot", null, true)); } catch (Exception e) { disconnected(); } }); return; }
            JSONObject updates = envelope.getJSONObject("data"); for (java.util.Iterator<String> it = updates.keys(); it.hasNext();) { String key = it.next(); if (Set.of("build", "goals", "session", "theme", "holdings").contains(key)) snapshot.put(key, updates.get(key)); }
            snapshot.put("revision", envelope.getLong("revision")); snapshot.put("at", envelope.getString("at"));
        }
        revision = envelope.getLong("revision"); connected = true;
        ui.post(() -> { if (snapshot != null) { pairing.setVisibility(View.GONE); status.setText("Connected"); render(); } });
    }
    private synchronized void render() {
        if (snapshot == null || destroyed) return; int target = renderedDestination == destination ? scroll.getScrollY() : scrollPositions[destination]; renderedDestination=destination;content.removeAllViews();
        try {
            context.setText(snapshot.getString("contextLabel")); JSONObject section = snapshot.getJSONObject(destinations[destination]); content.addView(text(section.getString("title"), 20, "#E4C777"));
            JSONArray rows = section.getJSONArray("rows"); for (int i = 0; i < rows.length(); i++) addRow(rows.getJSONObject(i), destinations[destination] + i);
            if (destination == 0) { JSONArray holdings = snapshot.getJSONObject("holdings").getJSONArray("rows"); for (int i = 0; i < holdings.length(); i++) addRow(holdings.getJSONObject(i), "holding" + i); }
            if (rows.length() == 0) content.addView(text("No supported evidence in this selection.", 14, "#C7E4E9"));
            String at=java.time.OffsetDateTime.parse(snapshot.getString("at")).atZoneSameInstant(java.time.ZoneId.systemDefault()).format(java.time.format.DateTimeFormatter.ofPattern("MMM d HH:mm:ss"));freshness.setText("PC snapshot "+at+" · revision "+revision);
            JSONObject theme = snapshot.getJSONObject("theme"); root.setBackgroundColor(Color.parseColor(theme.optString("background", "#081621")));
            getWindow().getDecorView().setBackgroundColor(Color.parseColor(theme.optString("background","#081621")));applyTextTheme(root);
            if(destination>0){String history=destination==1?"loot":"session";for(JSONObject entry:historyRows.getOrDefault(history,java.util.Collections.emptyList()))addRow(entry,"history"+entry.optString("id"));content.addView(button(destination==1?"Older session loot":"More session history",()->loadHistory(history)));}
            scroll.post(() -> scroll.scrollTo(0, target));
        } catch (Exception e) { status.setText("Snapshot details unavailable"); }
    }
    private void applyTextTheme(android.view.View view){if(view instanceof TextView&&view.getTag() instanceof String)((TextView)view).setTextColor(Color.parseColor(theme((String)view.getTag(),"#e8f8fc")));if(view instanceof Button&&view.getBackground() instanceof android.graphics.drawable.GradientDrawable){android.graphics.drawable.GradientDrawable shape=(android.graphics.drawable.GradientDrawable)view.getBackground();shape.setColor(Color.parseColor(theme("surface","#0c1c29")));shape.setStroke(dp(view.isSelected()?2:1),Color.parseColor(theme(view.isSelected()?"accent":"line","#38585d")));}if(view instanceof android.view.ViewGroup){android.view.ViewGroup group=(android.view.ViewGroup)view;for(int i=0;i<group.getChildCount();i++)applyTextTheme(group.getChildAt(i));}}
    private void loadHistory(String kind){if(connection==null||snapshot==null||!connected)return;String selection=snapshot.optString("contextLabel");int offset=historyOffsets.getOrDefault(kind,kind.equals("loot")?50:100);network.execute(()->{try{JSONObject page=connection.request("/api/v1/history?kind="+kind+"&offset="+offset,null,true);ui.post(()->{if(snapshot==null||!snapshot.optString("contextLabel").equals(selection))return;JSONArray rows=page.optJSONArray("rows");java.util.List<JSONObject> savedRows=historyRows.computeIfAbsent(kind,k->new java.util.ArrayList<>());if(rows!=null){for(int i=0;i<rows.length();i++)savedRows.add(rows.optJSONObject(i));historyOffsets.put(kind,offset+rows.length());}render();if(!page.optBoolean("more"))showStatus("All retained "+kind+" history loaded");});}catch(Exception error){showStatus("History unavailable; reconnect to the PC");}});}
    private void addRow(JSONObject row, String fallback) {
        String key = row.optString("id", fallback); if (key.isEmpty()) key = fallback; final String identity = destinations[destination] + ":" + key;
        content.addView(text(row.optString("label"), 16, "#E8F8FC")); content.addView(text(row.optString("value", "Unknown"), 14, "#7BE8C5"));
        String detail = row.optString("detail"); if (!detail.isEmpty()) {
            TextView explanation = text(detail, 13, "#C7E4E9"); explanation.setVisibility(expanded.contains(identity) ? View.VISIBLE : View.GONE);
            Button disclosure = button(expanded.contains(identity) ? "Hide details" : "Why / details", () -> { boolean open = explanation.getVisibility() != View.VISIBLE; explanation.setVisibility(open ? View.VISIBLE : View.GONE); if (open) expanded.add(identity); else expanded.remove(identity); }); content.addView(disclosure); content.addView(explanation);
        }
        View separator = new View(this); separator.setBackgroundColor(Color.parseColor("#38585D")); content.addView(separator, new LinearLayout.LayoutParams(-1, 1));
    }
    private void discover() {
        if (discovery != null) return; discovered.removeAllViews(); discovered.addView(text("Catalyst hosts on this LAN", 18, "#E4C777")); nsd = (NsdManager) getSystemService(NSD_SERVICE);
        discovery = new NsdManager.DiscoveryListener() {
            public void onDiscoveryStarted(String serviceType) { showStatus("Discovering local PCs…"); }
            public void onServiceFound(NsdServiceInfo info) {
                if (!info.getServiceType().equals("_catalyst._tcp.")) return;
                nsd.resolveService(info, new NsdManager.ResolveListener() {
                    public void onResolveFailed(NsdServiceInfo service, int code) { showStatus("Discovery blocked; use manual address or pairing QR"); }
                    public void onServiceResolved(NsdServiceInfo service) {
                        byte[] id = service.getAttributes().get("server"), protocol = service.getAttributes().get("protocol");
                        if (id == null || protocol == null || !"1".equals(new String(protocol, StandardCharsets.UTF_8))) return;
                        String server = new String(id, StandardCharsets.UTF_8); String address = "https://" + service.getHost().getHostAddress() + ":" + service.getPort();
                        ui.post(() -> discovered.addView(button("Catalyst · " + address, () -> {
                            if (saved == null || !server.equals(saved.optString("serverId"))) { showStatus("Discovery is unverified. Scan this PC’s pairing QR."); pairingControls(); return; }
                            network.execute(() -> { try { HostConnection candidate = HostConnection.trusted(address, server, saved.getString("certificate"), null); candidate.verifyIdentity(); candidate.close(); saved.put("address", address); secrets.save(saved.toString()); ui.post(MainActivity.this::connect); } catch (Exception e) { showStatus("Discovered identity could not be verified. Credentials were not sent."); } });
                        })));
                    }
                });
            }
            public void onServiceLost(NsdServiceInfo info) { }
            public void onDiscoveryStopped(String serviceType) { discovery = null; }
            public void onStartDiscoveryFailed(String serviceType, int code) { discovery = null; showStatus("Discovery unavailable. Use the manual address."); }
            public void onStopDiscoveryFailed(String serviceType, int code) { discovery = null; }
        };
        nsd.discoverServices("_catalyst._tcp.", NsdManager.PROTOCOL_DNS_SD, discovery);
        ui.postDelayed(() -> { if (discovery != null) try { nsd.stopServiceDiscovery(discovery); } catch (IllegalArgumentException ignored) { discovery = null; } }, 15000);
    }
    @Override protected void onDestroy() { destroyed = true; ui.removeCallbacksAndMessages(null); if (stream != null) stream.cancel(); if (connection != null) connection.close(); if (discovery != null) try { nsd.stopServiceDiscovery(discovery); } catch (IllegalArgumentException ignored) { } network.shutdownNow(); super.onDestroy(); }
}
