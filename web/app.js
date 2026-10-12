(() => {
  'use strict';
  const icons = {
    mouse: '<rect x="6" y="2" width="12" height="20" rx="6"/><path d="M12 2v7M6 10h12"/>',
    grid: '<rect x="3" y="3" width="7" height="7" rx="1.4"/><rect x="14" y="3" width="7" height="7" rx="1.4"/><rect x="3" y="14" width="7" height="7" rx="1.4"/><rect x="14" y="14" width="7" height="7" rx="1.4"/>',
    activity: '<path d="M3 12h4l3-8 4 16 3-8h4"/>',
    settings: '<path d="m9 4 1-2h4l1 2 2 1 2-.1 2 3.5-1 1.6v3l1 1.5-2 3.5-2-.1-2 1-1 2h-4l-1-2-2-1-2 .1-2-3.5 1-1.5v-3L3 8.5 5 5l2 .1z"/><circle cx="12" cy="11.5" r="3"/>',
    sparkles: '<path d="m12 3 2.3 6.7L21 12l-6.7 2.3L12 21l-2.3-6.7L3 12l6.7-2.3zM20 2v4m-2-2h4"/>',
    monitor: '<rect x="3" y="3" width="18" height="13" rx="2"/><path d="M8 21h8m-4-5v5"/>',
    bolt: '<path d="m13 2-9 12h7l-1 8 10-13h-7z"/>',
    arrowUpRight: '<path d="M5 19 19 5M7 5h12v12"/>',
    arrowRight: '<path d="M4 12h16m-6-6 6 6-6 6"/>',
    plus: '<path d="M12 5v14M5 12h14"/>',
    shield: '<path d="m12 2 8 3v6c0 5-5 8-8 10-3-2-8-5-8-10V5z"/><path d="m8 11 3 3 5-5"/>',
    close: '<path d="m6 6 12 12M6 18 18 6"/>',
    app: '<rect x="3" y="3" width="18" height="18" rx="3"/><path d="M3 8h18M7 5.5h.01m3 0h.01"/>',
    globe: '<circle cx="12" cy="12" r="9"/><path d="M3 12h18M12 3c-5 5-5 13 0 18 5-5 5-13 0-18"/>',
    folder: '<path d="M3 7V5a2 2 0 0 1 2-2h5l2 3h7a2 2 0 0 1 2 2v11a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z"/>',
    clock: '<circle cx="12" cy="12" r="9"/><path d="M12 7v5l3 2"/>',
    edit: '<path d="m15 4 5 5M4 20l5-1L21 7a2 2 0 0 0-5-5L4 14z"/>',
    trash: '<path d="M3 6h18M9 6V3h6v3M5 6l1 15h12l1-15M10 10v7m4-7v7"/>',
    play: '<path d="m8 4 12 8-12 8z"/>',
    check: '<path d="m5 12 4 4L19 6"/>',
    warning: '<path d="m12 3 10 18H2zM12 9v5m0 3h.01"/>',
    wheelUp: '<path d="m7 8 5-5 5 5M12 3v12M6 19h12"/>',
    wheelDown: '<path d="m7 16 5 5 5-5M12 21V9M6 5h12"/>',
    back: '<path d="m14 5-7 7 7 7"/>',
    forward: '<path d="m10 5 7 7-7 7"/>',
    cursor: '<path d="m5 3 15 10-7 1-3 7z"/>'
  };
  const gestures = {
    left: { label: 'Left click', short: 'Left', icon: 'mouse' },
    right: { label: 'Right click', short: 'Right', icon: 'mouse' },
    middle: { label: 'Middle click', short: 'Middle', icon: 'mouse' },
    wheelUp: { label: 'Wheel up', short: 'Scroll up', icon: 'wheelUp' },
    wheelDown: { label: 'Wheel down', short: 'Scroll down', icon: 'wheelDown' },
    back: { label: 'Back button', short: 'Back', icon: 'back' },
    forward: { label: 'Forward button', short: 'Forward', icon: 'forward' }
  };
  const $ = id => document.getElementById(id);
  const icon = name => `<svg viewBox="0 0 24 24" aria-hidden="true">${icons[name] || icons.app}</svg>`;
  const escape = value => String(value ?? '').replace(/[&<>"']/g, char => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[char]));
  document.querySelectorAll('[data-icon]').forEach(el => { el.innerHTML = icon(el.dataset.icon); });

  let token = '';
  const hashToken = new URLSearchParams(location.hash.slice(1)).get('token');
  try {
    token = hashToken || sessionStorage.getItem('mousepilot-token') || '';
    if (hashToken) sessionStorage.setItem('mousepilot-token', hashToken);
  } catch (_) { token = hashToken || ''; }
  if (location.hash) history.replaceState(null, '', location.pathname + location.search);
  let state = { enabled: true, hookActive: false, rules: [], apps: [], activity: [], startup: false, version: '1.0' };
  let connected = false;
  let initialLoaded = false;
  let stateSignature = '';
  let currentView = 'shortcuts';
  let editing = null;
  let sequence = [];
  let actionKind = 'app';
  let busy = false;
  let stateRequestPending = false;
  let quitting = false;
  let confirmationAction = null;
  let practiceSequence = [];
  let practiceLastInput = 0;
  let practiceTimer = null;
  let lastWheelTime = 0;

  async function api(path, payload) {
    const controller = new AbortController();
    const timeout = setTimeout(() => controller.abort(), path === '/api/browse' ? 180000 : 10000);
    try {
      const response = await fetch(path, {
        method: payload === undefined ? 'GET' : 'POST',
        headers: { 'Content-Type': 'application/json', 'X-MousePilot-Token': token },
        body: payload === undefined ? undefined : JSON.stringify(payload),
        cache: 'no-store', signal: controller.signal
      });
      let data;
      try { data = await response.json(); } catch (_) { data = null; }
      if (!response.ok) throw new Error(data?.error || (response.status === 401 || response.status === 403 ? 'This dashboard link has expired. Open MousePilot again to reconnect.' : `Could not complete that action (${response.status}).`));
      if (data?.error) throw new Error(data.error);
      return data;
    } catch (error) {
      if (error.name === 'AbortError') throw new Error('MousePilot took too long to respond. Please try again.');
      if (error instanceof TypeError) throw new Error('The background app is not connected. Open MousePilot to reconnect.');
      throw error;
    } finally { clearTimeout(timeout); }
  }

  function updateConnection() {
    const pill = $('connection-pill');
    pill.classList.toggle('offline', !connected);
    pill.classList.toggle('paused', connected && (!state.enabled || !state.hookActive));
    $('connection-label').textContent = quitting ? 'MousePilot stopped' : !connected ? (initialLoaded ? 'Disconnected' : 'Connecting…') : !state.hookActive ? 'Listener unavailable' : !state.enabled ? 'Shortcuts paused' : 'Running locally';
    $('global-toggle').disabled = !connected || busy;
    $('startup-toggle').disabled = !connected || busy;
  }

  function acceptState(data) {
    if (!data || !Array.isArray(data.rules) || !Array.isArray(data.apps)) return;
    state = { ...state, ...data };
    connected = true;
    initialLoaded = true;
    $('error-banner').hidden = true;
    const signature = JSON.stringify(state);
    if (signature !== stateSignature) {
      stateSignature = signature;
      renderState();
    }
    updateConnection();
  }

  async function refresh() {
    if (stateRequestPending || quitting) return;
    stateRequestPending = true;
    try { acceptState(await api('/api/state')); }
    catch (error) {
      connected = false;
      $('error-banner').textContent = error.message;
      $('error-banner').hidden = false;
      if (!initialLoaded) $('rules-container').innerHTML = `<div class="empty-state"><span class="stat-icon violet">${icon('monitor')}</span><h3 style="margin-top:18px">Let’s connect your mouse</h3><p>Open MousePilot using its launcher to bring your shortcuts online. This dashboard reconnects automatically.</p></div>`;
      updateConnection();
    } finally { stateRequestPending = false; }
  }

  function totalRuns() { return state.rules.reduce((sum, rule) => sum + Number(rule.runCount || 0), 0); }
  function gestureChip(type, removable = false, index = 0) {
    const gesture = gestures[type];
    if (!gesture) return '';
    return `<span class="gesture-chip" title="${gesture.label}">${icon(gesture.icon)}${gesture.short}${removable ? `<button type="button" class="sequence-remove" data-remove="${index}" aria-label="Remove ${gesture.label} at position ${index + 1}">×</button>` : ''}</span>`;
  }
  function sequenceHtml(items, removable = false) { return items.map((item, index) => `${index ? '<span class="sequence-arrow" aria-hidden="true">›</span>' : ''}${gestureChip(item, removable, index)}`).join(''); }
  function appFor(rule) { return state.apps.find(app => app.id === rule.action?.appId); }
  function actionLabel(rule) {
    if (rule.action?.kind === 'url') {
      try { return new URL(rule.action.url).hostname; } catch (_) { return rule.action.url || 'Website'; }
    }
    return appFor(rule)?.name || 'Application unavailable';
  }
  function claudeApp() { return state.apps.find(app => /claude/i.test(app.name)); }

  function renderState() {
    $('nav-count').textContent = state.rules.length;
    $('rule-count').textContent = state.rules.length;
    $('stat-active').textContent = state.enabled ? state.rules.filter(rule => rule.enabled).length : 0;
    $('stat-saved').textContent = state.rules.length;
    $('stat-runs').textContent = totalRuns().toLocaleString();
    $('global-toggle').setAttribute('aria-checked', String(!!state.enabled));
    $('startup-toggle').setAttribute('aria-checked', String(!!state.startup));
    $('engine-caption').textContent = state.enabled ? 'Shortcuts enabled' : 'All shortcuts paused';
    $('version').textContent = state.version || '1.0';
    const container = $('rules-container');
    if (!state.rules.length) {
      container.innerHTML = `<div class="empty-state"><div class="empty-illustration" aria-hidden="true"><span class="empty-mouse"></span><span class="orbit-mark">${icon('sparkles')}</span><span class="float-chip one">click, click…</span><span class="float-chip two">and you’re there ↗</span></div><h3>Your first shortcut starts here</h3><p>Open an app or a favorite website with a mouse gesture.<br>A few clicks to set up. Ready as soon as you save.</p><div class="empty-actions"><button class="button primary" data-action="new">${icon('plus')} Create a shortcut</button>${claudeApp() ? `<button class="text-button" data-action="claude">Make a Claude shortcut ${icon('arrowRight')}</button>` : `<button class="text-button" data-action="try">Try a gesture first ${icon('arrowRight')}</button>`}</div></div>`;
    } else {
      container.innerHTML = state.rules.map(rule => {
        const label = actionLabel(rule);
        return `<article class="rule-card ${rule.enabled ? '' : 'disabled'}" data-id="${escape(rule.id)}"><div class="rule-top"><span class="app-tile ${rule.action?.kind === 'url' ? 'web-tile' : ''}">${rule.action?.kind === 'url' ? icon('globe') : escape(label.slice(0, 1).toUpperCase())}</span><div class="rule-info"><h3 title="${escape(rule.name)}">${escape(rule.name)}</h3><p title="${escape(label)}">Open ${escape(label)}</p></div><button class="toggle" role="switch" aria-label="Enable ${escape(rule.name)}" aria-checked="${!!rule.enabled}" data-action="rule-toggle"><span></span></button></div><div class="rule-gesture" aria-label="Gesture: ${escape(rule.sequence.map(type => gestures[type]?.label || type).join(', '))}">${sequenceHtml(rule.sequence)}</div><div class="rule-card-footer"><span class="rule-timing">${icon('clock')}${Number(rule.gapMs) / 1000}s between clicks</span><div class="rule-actions"><button class="text-button" data-action="test" title="Open this app or website now">${icon('play')} Test action</button><button class="icon-button" data-action="practice-rule" aria-label="Practice ${escape(rule.name)}" title="Practice gesture">${icon('mouse')}</button><button class="icon-button" data-action="edit" aria-label="Edit ${escape(rule.name)}" title="Edit shortcut">${icon('edit')}</button><button class="icon-button" data-action="delete" aria-label="Delete ${escape(rule.name)}" title="Delete shortcut">${icon('trash')}</button></div></div></article>`;
      }).join('');
    }
    renderActivity($('recent-activity'), 3);
    renderActivity($('all-activity'));
  }

  function humanTime(value) {
    const date = new Date(value);
    if (!Number.isFinite(date.getTime())) return '';
    const seconds = (Date.now() - date.getTime()) / 1000;
    if (seconds < 45) return 'Just now';
    if (seconds < 3600) return `${Math.floor(seconds / 60)}m ago`;
    if (seconds < 86400) return `${Math.floor(seconds / 3600)}h ago`;
    return date.toLocaleDateString(undefined, { month: 'short', day: 'numeric' });
  }
  function renderActivity(container, limit) {
    const entries = [...(state.activity || [])].sort((a, b) => new Date(b.time) - new Date(a.time)).slice(0, limit);
    if (!entries.length) {
      container.innerHTML = `<div class="activity-empty">${icon('activity')}<div><strong>A quiet start. Your next move goes here.</strong><p>Shortcut activity will appear as you use MousePilot.</p></div></div>`;
      return;
    }
    container.innerHTML = entries.map(entry => `<div class="activity-row ${entry.kind === 'error' ? 'error' : ''}"><span class="activity-entry-icon">${icon(entry.kind === 'error' ? 'warning' : entry.kind === 'run' ? 'arrowUpRight' : 'check')}</span><span class="activity-entry">${escape(entry.message)}</span><time datetime="${escape(entry.time)}" title="${escape(new Date(entry.time).toLocaleString())}">${escape(humanTime(entry.time))}</time></div>`).join('');
  }

  function setView(view) {
    if (!['shortcuts', 'activity', 'settings'].includes(view)) return;
    currentView = view;
    const headings = { shortcuts: ['Your mouse. Your shortcuts.', 'Turn a few clicks into your favorite next move.'], activity: ['Your shortcuts, in motion.', 'See what your gestures have been up to.'], settings: ['Settle into your flow.', 'The little details that make MousePilot yours.'] };
    document.querySelectorAll('.view-section').forEach(section => { section.hidden = section.id !== `view-${view}`; });
    document.querySelectorAll('.nav-item').forEach(button => {
      button.classList.toggle('active', button.dataset.view === view);
      if (button.dataset.view === view) button.setAttribute('aria-current', 'page'); else button.removeAttribute('aria-current');
    });
    $('breadcrumb-current').textContent = view.charAt(0).toUpperCase() + view.slice(1);
    $('page-title').textContent = headings[view][0];
    $('page-description').textContent = headings[view][1];
    document.querySelector('.new-rule-button').hidden = view === 'settings';
  }

  function renderAppOptions(selectedId = '') {
    $('rule-app').innerHTML = '<option value="">Choose an application…</option>' + [...state.apps].sort((a, b) => a.name.localeCompare(b.name)).map(app => `<option value="${escape(app.id)}">${escape(app.name)}</option>`).join('');
    $('rule-app').value = selectedId;
  }
  function setActionKind(kind) {
    actionKind = kind;
    document.querySelectorAll('[data-kind]').forEach(button => { button.classList.toggle('active', button.dataset.kind === kind); button.setAttribute('aria-pressed', String(button.dataset.kind === kind)); });
    $('app-action').hidden = kind !== 'app';
    $('url-action').hidden = kind !== 'url';
  }
  function renderSequenceBuilder() {
    $('sequence-builder').innerHTML = sequence.length ? sequenceHtml(sequence, true) : `<span class="sequence-placeholder">${icon('cursor')} Your gesture goes here</span>`;
    $('gesture-buttons').querySelectorAll('button').forEach(button => { button.disabled = sequence.length >= 8; });
  }
  function openRule(rule = null, template = false) {
    if (!connected) { toast('Open MousePilot to connect the dashboard first.', true); return; }
    editing = rule ? { ...rule } : null;
    sequence = rule ? [...rule.sequence] : template ? ['left', 'left', 'left', 'right'] : [];
    const claude = template ? claudeApp() : null;
    $('rule-dialog-title').textContent = rule ? 'Edit your shortcut' : 'Create a shortcut';
    $('save-rule').innerHTML = `${rule ? 'Save changes' : 'Create shortcut'} <span>${icon('arrowRight')}</span>`;
    $('rule-name').value = rule?.name || (claude ? 'Open Claude' : '');
    $('rule-url').value = rule?.action?.kind === 'url' ? rule.action.url : '';
    const gap = String(rule?.gapMs || 700);
    if (!$('rule-gap').querySelector(`option[value="${Number(gap)}"]`)) $('rule-gap').add(new Option(`${Number(gap) / 1000} seconds`, gap));
    $('rule-gap').value = gap;
    $('form-error').hidden = true;
    renderAppOptions(rule?.action?.appId || claude?.id || '');
    setActionKind(rule?.action?.kind || 'app');
    renderSequenceBuilder();
    $('rule-dialog').showModal();
    $('rule-name').focus();
  }
  async function saveRule(event) {
    event.preventDefault();
    if (busy) return;
    const name = $('rule-name').value.trim();
    const url = $('rule-url').value.trim();
    const appId = $('rule-app').value;
    let error = '';
    if (!name) error = 'Give your shortcut a name first.';
    else if (!sequence.length) error = 'Choose at least one mouse gesture.';
    else if (actionKind === 'app' && !appId) error = 'Choose an app, or use Browse to find one.';
    else if (actionKind === 'url') {
      try { if (!['http:', 'https:'].includes(new URL(url).protocol)) error = 'Use a full website address beginning with https:// or http://.'; }
      catch (_) { error = 'Use a full website address, like https://claude.ai.'; }
    }
    if (error) { $('form-error').textContent = error; $('form-error').hidden = false; return; }
    busy = true;
    $('save-rule').disabled = true;
    $('form-error').hidden = true;
    try {
      const rule = { ...(editing || {}), name, sequence: [...sequence], gapMs: Number($('rule-gap').value), enabled: editing ? !!editing.enabled : true, action: actionKind === 'app' ? { kind: 'app', appId } : { kind: 'url', url } };
      acceptState(await api('/api/rules', rule));
      $('rule-dialog').close();
      toast(editing ? 'Shortcut updated. Your changes are ready.' : 'Shortcut created. Click into another app to use it.');
      await refresh();
    } catch (error) { $('form-error').textContent = error.message; $('form-error').hidden = false; }
    finally { busy = false; $('save-rule').disabled = false; updateConnection(); }
  }

  function toast(message, isError = false) {
    const node = document.createElement('div');
    node.className = `toast${isError ? ' error' : ''}`;
    node.innerHTML = `<span>${icon(isError ? 'warning' : 'check')}</span><span>${escape(message)}</span><button aria-label="Dismiss message">${icon('close')}</button>`;
    node.querySelector('button').addEventListener('click', () => node.remove());
    $('toast-container').appendChild(node);
    setTimeout(() => node.remove(), isError ? 12000 : 5500);
  }

  async function mutate(path, payload, message) {
    if (busy || !connected) return;
    busy = true;
    updateConnection();
    try { acceptState(await api(path, payload)); if (message) toast(message); await refresh(); }
    catch (error) { toast(error.message, true); }
    finally { busy = false; updateConnection(); }
  }

  function confirmAction(title, description, label, action) {
    $('confirm-title').textContent = title;
    $('confirm-description').textContent = description;
    $('confirm-button').textContent = label;
    confirmationAction = action;
    $('confirm-dialog').showModal();
  }

  function practiceRule() {
    return state.rules.find(rule => rule.id === $('practice-rule').value) || { sequence: ['left', 'left', 'left', 'right'], gapMs: 700 };
  }
  function resetPractice() {
    clearTimeout(practiceTimer);
    practiceSequence = [];
    practiceLastInput = 0;
    $('practice-pad').classList.remove('success', 'mismatch');
    $('practice-message').textContent = 'Make your gesture inside this area';
    $('practice-subtitle').textContent = `Keep each move within ${practiceRule().gapMs / 1000} seconds of the last.`;
    $('practice-progress').innerHTML = '';
    $('practice-target').innerHTML = sequenceHtml(practiceRule().sequence);
  }
  function openPractice(id) {
    $('practice-rule').innerHTML = state.rules.length ? state.rules.map(rule => `<option value="${escape(rule.id)}">${escape(rule.name)}</option>`).join('') : '<option value="example">Example · three left clicks, one right click</option>';
    if (id) $('practice-rule').value = id;
    resetPractice();
    $('practice-dialog').showModal();
  }
  function practiceInput(type) {
    if (!$('practice-dialog').open) return;
    const now = performance.now();
    const rule = practiceRule();
    if (now - practiceLastInput > rule.gapMs || practiceSequence.length >= rule.sequence.length || $('practice-pad').classList.contains('mismatch')) practiceSequence = [];
    practiceLastInput = now;
    practiceSequence.push(type);
    clearTimeout(practiceTimer);
    const matchedSoFar = practiceSequence.every((value, index) => value === rule.sequence[index]);
    $('practice-progress').innerHTML = sequenceHtml(practiceSequence);
    $('practice-pad').classList.remove('success', 'mismatch');
    if (!matchedSoFar) {
      $('practice-pad').classList.add('mismatch');
      $('practice-message').textContent = 'Almost. Give that sequence another go.';
      $('practice-subtitle').textContent = 'Your next click starts a fresh attempt.';
    } else if (practiceSequence.length === rule.sequence.length) {
      $('practice-pad').classList.add('success');
      $('practice-message').textContent = 'That’s it. Gesture matched!';
      $('practice-subtitle').textContent = 'Use it outside this dashboard to run your shortcut.';
    } else {
      $('practice-message').textContent = 'Looking good. Keep going…';
      $('practice-subtitle').textContent = `${practiceSequence.length} of ${rule.sequence.length} gestures`;
      practiceTimer = setTimeout(() => {
        $('practice-pad').classList.add('mismatch');
        $('practice-message').textContent = 'A little quicker between gestures.';
        $('practice-subtitle').textContent = 'Your next click starts a fresh attempt.';
      }, rule.gapMs);
    }
  }

  $('gesture-buttons').innerHTML = Object.entries(gestures).map(([type, gesture]) => `<button type="button" class="gesture-button" data-gesture="${type}">${icon(gesture.icon)}${gesture.short}</button>`).join('');
  $('rule-form').addEventListener('submit', saveRule);
  $('rule-app').addEventListener('change', () => { if (!$('rule-name').value.trim()) { const app = state.apps.find(item => item.id === $('rule-app').value); if (app) $('rule-name').value = `Open ${app.name}`.slice(0, 60); } });
  $('practice-rule').addEventListener('change', resetPractice);
  $('practice-dialog').addEventListener('close', () => clearTimeout(practiceTimer));
  $('practice-pad').addEventListener('contextmenu', event => event.preventDefault());
  $('practice-pad').addEventListener('mousedown', event => event.preventDefault());
  $('practice-pad').addEventListener('mouseup', event => { event.preventDefault(); const type = ({ 0: 'left', 1: 'middle', 2: 'right', 3: 'back', 4: 'forward' })[event.button]; if (type) practiceInput(type); });
  $('practice-pad').addEventListener('auxclick', event => event.preventDefault());
  $('practice-pad').addEventListener('wheel', event => { event.preventDefault(); const now = performance.now(); if (now - lastWheelTime < 100 || !event.deltaY) return; lastWheelTime = now; practiceInput(event.deltaY < 0 ? 'wheelUp' : 'wheelDown'); }, { passive: false });
  $('confirm-button').addEventListener('click', async () => { const action = confirmationAction; confirmationAction = null; $('confirm-dialog').close(); if (action) await action(); });

  document.addEventListener('click', async event => {
    const button = event.target.closest('button, a.brand');
    if (!button) return;
    if (button.matches('a.brand')) { event.preventDefault(); setView('shortcuts'); return; }
    if (button.dataset.view) { setView(button.dataset.view); return; }
    if (button.dataset.kind) { setActionKind(button.dataset.kind); return; }
    if (button.dataset.gesture) { if (sequence.length < 8) sequence.push(button.dataset.gesture); renderSequenceBuilder(); return; }
    if (button.dataset.remove !== undefined) { sequence.splice(Number(button.dataset.remove), 1); renderSequenceBuilder(); return; }
    const action = button.dataset.action;
    const id = button.closest('[data-id]')?.dataset.id;
    const rule = id ? state.rules.find(item => item.id === id) : null;
    if (action === 'new') openRule();
    if (action === 'claude') openRule(null, true);
    if (action === 'edit' && rule) openRule(rule);
    if (action === 'close-rule' && !busy) $('rule-dialog').close();
    if (action === 'clear-sequence') { sequence = []; renderSequenceBuilder(); }
    if (action === 'try') openPractice();
    if (action === 'practice-rule' && rule) openPractice(rule.id);
    if (action === 'close-practice') $('practice-dialog').close();
    if (action === 'reset-practice') resetPractice();
    if (action === 'cancel-confirm') $('confirm-dialog').close();
    if (action === 'global-toggle') await mutate('/api/toggle', { enabled: !state.enabled }, state.enabled ? 'All shortcuts are paused.' : 'Shortcuts are enabled.');
    if (action === 'startup-toggle') await mutate('/api/startup', { enabled: !state.startup }, state.startup ? 'MousePilot will no longer start with Windows.' : 'MousePilot will start when you sign in.');
    if (action === 'rule-toggle' && rule) await mutate('/api/rules', { ...rule, enabled: !rule.enabled });
    if (action === 'test' && rule) await mutate('/api/test', { id: rule.id }, `Opened ${actionLabel(rule)}.`);
    if (action === 'delete' && rule) confirmAction(`Delete “${rule.name}”?`, 'This removes the shortcut immediately. You can create it again anytime.', 'Delete shortcut', () => mutate('/api/delete', { id: rule.id }, 'Shortcut deleted.'));
    if (action === 'browse') {
      button.disabled = true;
      try {
        const result = await api('/api/browse', {});
        if (result?.app) {
          if (!state.apps.some(app => app.id === result.app.id)) state.apps.push(result.app);
          renderAppOptions(result.app.id);
          if (!$('rule-name').value.trim()) $('rule-name').value = `Open ${result.app.name}`.slice(0, 60);
        }
      } catch (error) { $('form-error').textContent = error.message; $('form-error').hidden = false; }
      finally { button.disabled = false; }
    }
    if (action === 'quit') confirmAction('Quit MousePilot?', 'All mouse shortcuts will stop until you open MousePilot again.', 'Quit MousePilot', async () => {
      try { await api('/api/quit', {}); quitting = true; connected = false; updateConnection(); toast('MousePilot stopped. You can close this dashboard.'); }
      catch (error) { toast(error.message, true); }
    });
  });

  function sendPresence(focused = document.hasFocus() && !document.hidden) {
    if (quitting || !token) return;
    fetch('/api/presence', { method: 'POST', headers: { 'Content-Type': 'application/json', 'X-MousePilot-Token': token }, body: JSON.stringify({ focused }), keepalive: true }).catch(() => {});
  }
  window.addEventListener('focus', () => sendPresence());
  window.addEventListener('blur', () => sendPresence(false));
  document.addEventListener('visibilitychange', () => sendPresence());
  window.addEventListener('pagehide', () => sendPresence(false));
  setInterval(() => { if (document.hasFocus() && !document.hidden) sendPresence(true); }, 1000);
  setInterval(refresh, 1500);
  setInterval(() => { if (connected) { renderActivity($('recent-activity'), 3); renderActivity($('all-activity')); } }, 30000);
  setView('shortcuts');
  sendPresence();
  refresh();
})();
