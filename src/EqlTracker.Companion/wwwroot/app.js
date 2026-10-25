'use strict';
(() => {
  const $ = id => document.getElementById(id);
  let challenge = new URLSearchParams(location.hash.slice(1)).get('pair');
  history.replaceState(null, '', location.pathname); // Bootstrap secrets never remain in navigation history.
  let snapshot = null, destination = 'build', socket, reconnect = 1000, reconnectTimer, csrf = '', searchTimer, offset = 0, query = '', pairing = false;
  const historyOffsets = { loot: 50, session: 100 };
  const historyRows = { loot: [], session: [] };
  let protocolMismatch = false;
  const status = text => { $('status').textContent = text; };
  async function api(path, options = {}) {
    const response = await fetch(path, { credentials: 'same-origin', cache: 'no-store', ...options, headers: { 'X-Catalyst-Protocol': '1', ...options.headers } });
    if (response.status === 426) throw Error('Protocol mismatch. Update this companion.');
    if (!response.ok) throw Error(response.status === 401 ? 'Pair this browser with your PC.' : `Connection unavailable (${response.status})`);
    return response.status === 204 || options.method === 'POST' && path.endsWith('logout') ? {} : response.json();
  }
  const post = (path, body) => api(path, { method: 'POST', headers: { 'Content-Type': 'application/json', 'X-Catalyst-CSRF': csrf }, body: JSON.stringify(body) });
  function row(value) {
    const article = document.createElement('article'), title = document.createElement('p'), amount = document.createElement('p');
    title.className = 'row-label'; title.textContent = value.label; amount.className = 'row-value'; amount.textContent = value.value; article.append(title, amount);
    if (value.detail) { const details = document.createElement('details'), summary = document.createElement('summary'), text = document.createElement('p'); summary.textContent = 'Why / details'; text.textContent = value.detail; details.append(summary, text); article.append(details); }
    return article;
  }
  function render() {
    const content = $('content'); const opened = new Set([...content.querySelectorAll('details[open]')].map(d => d.parentElement.dataset.key));
    $('history').hidden = destination === 'build'; $('history-more').textContent = destination === 'goals' ? 'Older session loot' : 'More session history';
    $('history-results').replaceChildren(...historyRows[destination === 'goals' ? 'loot' : 'session'].map(row));
    content.replaceChildren(); if (!snapshot) return;
    $('context').textContent = snapshot.contextLabel;
    const section = snapshot[destination]; const heading = document.createElement('h1'); heading.textContent = section.title; content.append(heading);
    const rows = [...section.rows, ...(destination === 'build' ? snapshot.holdings?.rows ?? [] : [])];
    rows.forEach((value, i) => { const node = row(value); node.dataset.key = value.id || `${destination}-${value.label}-${i}`; if (opened.has(node.dataset.key) && node.querySelector('details')) node.querySelector('details').open = true; content.append(node); });
    if (!rows.length) { const empty = document.createElement('p'); empty.className = 'empty'; empty.textContent = 'No supported evidence in this selection.'; content.append(empty); }
    for (const [key, value] of Object.entries(snapshot.theme || {})) if (/^(background|surface|text|secondary|accent|line|mint)$/.test(key) && /^#[0-9a-f]{6}$/i.test(value)) document.documentElement.style.setProperty(`--${key}`, value);
    $('freshness').textContent = `Revision ${snapshot.revision} · PC snapshot ${new Date(snapshot.at).toLocaleString()}`;
  }
  async function envelope(packet) {
    if (packet.protocol !== 1) { protocolMismatch = true; socket?.close(); status('Protocol mismatch. Update the companion.'); return; }
    if (packet.type === 'paused') { snapshot = null; historyRows.loot=[];historyRows.session=[]; $('content').replaceChildren(); $('history-results').replaceChildren(); $('results').replaceChildren();$('context').textContent = 'Sharing paused'; status(packet.status); return; }
    if (packet.type === 'snapshot') { if(!snapshot||snapshot.contextLabel!==packet.data.contextLabel){$('history-results').replaceChildren();historyRows.loot=[];historyRows.session=[];historyOffsets.loot=50;historyOffsets.session=100;}snapshot = packet.data; }
    else if (packet.type === 'update') { if (!snapshot || snapshot.revision !== packet.baseRevision) { await envelope(await api('/api/v1/snapshot')); return; } snapshot = { ...snapshot, ...packet.data, revision: packet.revision, at: packet.at }; }
    document.body.classList.remove('disconnected'); status(packet.status); render();
  }
  function stream() {
    socket = new WebSocket(`wss://${location.host}/api/v1/stream`, 'catalyst.v1');
    socket.onopen = () => { reconnect = 1000; };
    socket.onmessage = event => { if (event.data.length > 2_000_000) { socket.close(); return; } try { envelope(JSON.parse(event.data)).catch(error => status(error.message)); } catch { socket.close(); status('Invalid host response'); } };
    socket.onclose = () => { document.body.classList.add('disconnected'); clearTimeout(reconnectTimer); if(protocolMismatch){status('Protocol mismatch. Update the companion.');return;}status('Disconnected · showing last received snapshot'); reconnectTimer = setTimeout(connect, reconnect); reconnect = Math.min(reconnect * 2, 30000); };
    socket.onerror = () => status('Secure connection unavailable. Verify the PC and its certificate.');
  }
  async function connect() {
    try { const session = await api('/api/v1/session'); csrf = session.csrf; if (session.protocol !== 1) throw Error('Protocol mismatch'); $('pair').hidden = true; await envelope(await api('/api/v1/snapshot')); stream(); }
    catch (error) { status(error.message); $('pair').hidden = false; }
  }
  $('request').onclick = async () => {
    if (pairing || !challenge) { $('pair-status').textContent = 'Generate a fresh browser pairing link in PC settings.'; return; }
    pairing = true; $('request').disabled = true;
    try {
      const ticket = await post('/pair/request', { challenge, name: $('name').value.trim(), kind: 'browser', protocol: 1 }); challenge = null;
      const until = Date.now() + 120000;
      while (Date.now() < until) { const result = await post('/pair/poll', ticket); $('pair-status').textContent = `${result.status} · code ${ticket.requestId.slice(0,6).toUpperCase()}`; if (result.status === 'Approved') { await connect(); break; } if (result.status === 'Expired') break; await new Promise(resolve => setTimeout(resolve, 1500)); }
    } catch (error) { $('pair-status').textContent = error.message; }
    finally { pairing = false; $('request').disabled = false; }
  };
  document.querySelectorAll('nav button').forEach(button => { button.onclick = () => { destination = button.dataset.view; document.querySelectorAll('nav button').forEach(b => b.setAttribute('aria-pressed', String(b === button))); render(); }; });
  async function search(append) { try { const result = await api(`/api/v1/search?q=${encodeURIComponent(query)}&offset=${offset}`); if (!append) $('results').replaceChildren(); result.rows.forEach(value => $('results').append(row(value))); $('more').hidden = !result.more; offset += result.rows.length; } catch (error) { status(error.message); } }
  $('search').oninput = () => { clearTimeout(searchTimer); query = $('search').value.trim(); offset = 0; searchTimer = setTimeout(() => search(false), 250); };
  $('more').onclick = () => search(true);
  $('history-more').onclick = async () => { const kind = destination === 'goals' ? 'loot' : 'session'; try { const page = await api(`/api/v1/history?kind=${kind}&offset=${historyOffsets[kind]}`); if (!snapshot || page.contextId !== snapshot.contextId) return; historyRows[kind].push(...page.rows); historyOffsets[kind] += page.rows.length; render(); if (!page.more) $('history-more').textContent = 'All retained history loaded'; } catch (error) { status(error.message); } };
  $('disconnect').onclick = async () => { try { await post('/api/v1/logout', {}); socket?.close(); clearTimeout(reconnectTimer); snapshot = null; historyRows.loot=[];historyRows.session=[]; $('content').replaceChildren(); $('results').replaceChildren();$('history-results').replaceChildren();$('context').textContent='';$('freshness').textContent=''; $('pair').hidden = false; status('Unpaired'); } catch (error) { status(error.message); } };
  window.addEventListener('online', connect); connect();
})();
