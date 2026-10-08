(() => {
  'use strict';

  const dom = {
    root: document.documentElement,
    title: document.getElementById('title'),
    grid: document.getElementById('pads'),
    empty: document.getElementById('empty'),
    footer: document.getElementById('footer'),
    stopAll: document.getElementById('stopAll'),
    volumeWrap: document.getElementById('volumeWrap'),
    volume: document.getElementById('masterVolume'),
    volumeValue: document.getElementById('volumeValue'),
    meter: document.getElementById('meter'),
    meterFill: document.getElementById('meterFill'),
    status: document.getElementById('status'),
    statusDot: document.getElementById('statusDot'),
    statusText: document.getElementById('statusText'),
  };

  const pads = new Map();

  let socket = null;
  let retryDelay = 500;
  let wakeLock = null;
  let settings = null;
  let volumeDragging = false;

  function setStatus(text, kind) {
    dom.statusText.textContent = text;
    dom.statusDot.dataset.kind = kind;
  }

  function send(message) {
    if (!socket || socket.readyState !== WebSocket.OPEN) {
      return;
    }

    socket.send(JSON.stringify(message));
  }

  function connect() {
    setStatus('connecting', 'pending');

    const scheme = window.location.protocol === 'https:' ? 'wss' : 'ws';
    socket = new WebSocket(`${scheme}://${window.location.host}/ws`);

    socket.addEventListener('open', () => {
      retryDelay = 500;
      setStatus('connected', 'ok');
    });

    socket.addEventListener('message', (event) => {
      let message;

      try {
        message = JSON.parse(event.data);
      } catch {
        return;
      }

      handle(message);
    });

    socket.addEventListener('close', () => {
      setStatus('reconnecting', 'bad');

      for (const pad of pads.values()) {
        pad.classList.remove('is-active');
      }

      window.setTimeout(connect, retryDelay);
      retryDelay = Math.min(retryDelay * 2, 5000);
    });

    socket.addEventListener('error', () => socket.close());
  }

  function handle(message) {
    switch (message.type) {
      case 'hello':
        applyHello(message);
        break;

      case 'state':
        applyState(message);
        break;

      case 'error':
        console.warn('yasoundboard:', message.message);
        break;
    }
  }

  function applyHello(message) {
    settings = message.ui || {};

    document.title = message.title;
    dom.title.textContent = message.title;

    dom.root.style.setProperty('--accent', settings.accentColor || '#e24b4a');
    dom.root.style.setProperty('--pad-min', `${settings.padMinWidthPx || 92}px`);
    dom.root.style.setProperty('--gap', `${settings.padGapPx || 8}px`);
    dom.root.style.setProperty('--radius', `${settings.padRadiusPx || 14}px`);

    if (settings.columns > 0) {
      dom.root.style.setProperty('--columns', String(settings.columns));
      dom.grid.dataset.fixedColumns = 'true';
    } else {
      delete dom.grid.dataset.fixedColumns;
    }

    applyTheme(settings.theme);

    dom.meter.hidden = !settings.showLevels;
    dom.status.hidden = !settings.showConnectionStatus;
    dom.stopAll.hidden = !settings.showStopAll;
    dom.volumeWrap.hidden = !settings.showMasterVolume;
    dom.footer.hidden = !settings.showStopAll && !settings.showMasterVolume;

    buildPads(message.sounds);

    if (message.audio) {
      syncVolume(message.audio.masterGain);
    }

    requestWakeLock();
  }

  function applyState(message) {
    if (settings && settings.showLevels) {
      const level = Math.min(1, Math.max(0, message.peak || 0));
      dom.meterFill.style.transform = `scaleX(${level})`;
    }

    const active = new Set(message.active || []);

    for (const [id, pad] of pads) {
      pad.classList.toggle('is-active', active.has(id));
    }

    if (!volumeDragging) {
      syncVolume(message.masterGain);
    }
  }

  function buildPads(sounds) {
    dom.grid.replaceChildren();
    pads.clear();

    if (!sounds || sounds.length === 0) {
      dom.empty.hidden = false;
      return;
    }

    dom.empty.hidden = true;

    const fragment = document.createDocumentFragment();

    for (const sound of sounds) {
      const pad = document.createElement('button');
      pad.type = 'button';
      pad.className = 'pad';
      pad.dataset.id = sound.id;

      if (sound.color) {
        pad.style.setProperty('--pad-accent', sound.color);
      }

      if (settings.showLabels) {
        const label = document.createElement('span');
        label.className = 'pad-label';
        label.textContent = sound.name;
        pad.appendChild(label);
      }

      pad.addEventListener('pointerdown', (event) => {
        event.preventDefault();
        trigger(sound.id);
      });

      pad.addEventListener('contextmenu', (event) => event.preventDefault());

      fragment.appendChild(pad);
      pads.set(sound.id, pad);
    }

    dom.grid.appendChild(fragment);
  }

  function trigger(id) {
    send({ type: 'play', id });

    const pad = pads.get(id);
    if (!pad) {
      return;
    }

    if (settings && settings.flashOnTap) {
      pad.classList.remove('is-hit');
      void pad.offsetWidth;
      pad.classList.add('is-hit');
      window.setTimeout(() => pad.classList.remove('is-hit'), 150);
    }

    if (settings && settings.vibrateOnTap && navigator.vibrate) {
      navigator.vibrate(settings.vibrateMs || 15);
    }
  }

  function syncVolume(gain) {
    if (typeof gain !== 'number') {
      return;
    }

    const percent = Math.round(gain * 100);
    dom.volume.value = String(percent);
    dom.volumeValue.textContent = `${percent}%`;
  }

  function applyTheme(theme) {
    let resolved = 'dark';

    if (theme === 'auto') {
      resolved = window.matchMedia('(prefers-color-scheme: light)').matches ? 'light' : 'dark';
    } else if (theme === 'light') {
      resolved = 'light';
    }

    dom.root.dataset.theme = resolved;

    const meta = document.querySelector('meta[name="theme-color"]');
    if (meta) {
      meta.setAttribute('content', resolved === 'light' ? '#f3f4f6' : '#0f1014');
    }
  }

  async function requestWakeLock() {
    if (!settings || !settings.keepAwake) {
      return;
    }

    if (!('wakeLock' in navigator) || wakeLock) {
      return;
    }

    try {
      wakeLock = await navigator.wakeLock.request('screen');
      wakeLock.addEventListener('release', () => {
        wakeLock = null;
      });
    } catch {
      wakeLock = null;
    }
  }

  dom.volume.addEventListener('pointerdown', () => {
    volumeDragging = true;
  });

  dom.volume.addEventListener('input', () => {
    const percent = Number(dom.volume.value);
    dom.volumeValue.textContent = `${percent}%`;
    send({ type: 'setMasterGain', value: percent / 100 });
  });

  window.addEventListener('pointerup', () => {
    volumeDragging = false;
  });

  window.addEventListener('pointercancel', () => {
    volumeDragging = false;
  });

  dom.stopAll.addEventListener('click', () => send({ type: 'stopAll' }));

  document.addEventListener('visibilitychange', () => {
    if (document.visibilityState === 'visible') {
      requestWakeLock();
    }
  });

  connect();
})();
