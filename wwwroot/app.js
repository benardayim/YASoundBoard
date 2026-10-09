(function () {
  'use strict';

  var STORE_KEY = 'YASoundBoard.ui';

  var ACCENTS = ['#e24b4a', '#d4537e', '#7f77dd', '#378add', '#1d9e75', '#ef9f27'];

  var SCHEMA = [
    { key: 'theme', label: 'Theme', kind: 'select', value: 'auto', options: [['auto', 'Auto'], ['dark', 'Dark'], ['light', 'Light']] },
    { key: 'accentColor', label: 'Accent colour', kind: 'accent', value: '#e24b4a' },
    { key: 'columns', label: 'Pads per row', kind: 'select', numeric: true, value: 0, options: [['0', 'Automatic'], ['2', '2'], ['3', '3'], ['4', '4'], ['5', '5'], ['6', '6']] },
    { key: 'padMinWidthPx', label: 'Smallest pad', kind: 'range', value: 92, min: 48, max: 200, step: 4, suffix: ' px' },
    { key: 'padGapPx', label: 'Gap between pads', kind: 'range', value: 8, min: 0, max: 24, step: 1, suffix: ' px' },
    { key: 'padRadiusPx', label: 'Corner rounding', kind: 'range', value: 14, min: 0, max: 30, step: 1, suffix: ' px' },
    { key: 'showLabels', label: 'Show names on pads', kind: 'check', value: true },
    { key: 'showStopAll', label: 'Show stop all', kind: 'check', value: true },
    { key: 'showMasterVolume', label: 'Show volume', kind: 'check', value: true },
    { key: 'flashOnTap', label: 'Flash when tapped', kind: 'check', value: true },
    { key: 'vibrateOnTap', label: 'Vibrate when tapped', kind: 'check', value: true },
    { key: 'vibrateMs', label: 'Vibration length', kind: 'range', value: 15, min: 0, max: 120, step: 5, suffix: ' ms' },
    { key: 'keepAwake', label: 'Keep the screen on', kind: 'check', value: true }
  ];

  var dom = {
    root: document.documentElement,
    grid: document.getElementById('pads'),
    empty: document.getElementById('empty'),
    footer: document.getElementById('footer'),
    stopAll: document.getElementById('stopAll'),
    volumeWrap: document.getElementById('volumeWrap'),
    volume: document.getElementById('masterVolume'),
    volumeValue: document.getElementById('volumeValue'),
    settingsOpen: document.getElementById('settingsOpen'),
    settingsClose: document.getElementById('settingsClose'),
    settingsReset: document.getElementById('settingsReset'),
    settingsBody: document.getElementById('settingsBody'),
    panel: document.getElementById('panel')
  };

  var settings = {};
  var sounds = [];
  var pads = new Map();
  var isTouch = 'ontouchstart' in window;

  var socket = null;
  var retryDelay = 500;
  var wakeLock = null;
  var volumeDragging = false;
  var columnCount = 4;
  var accentStyle = null;

  function clearNode(node) {
    while (node.firstChild) {
      node.removeChild(node.firstChild);
    }
  }

  function load() {
    var raw = null;
    var stored = null;
    var index;
    var spec;

    try {
      raw = window.localStorage.getItem(STORE_KEY);
    } catch (error) {
      raw = null;
    }

    if (raw) {
      try {
        stored = JSON.parse(raw);
      } catch (error) {
        stored = null;
      }
    }

    settings = {};

    for (index = 0; index < SCHEMA.length; index++) {
      spec = SCHEMA[index];
      settings[spec.key] = stored && stored[spec.key] !== undefined ? stored[spec.key] : spec.value;
    }
  }

  function save() {
    try {
      window.localStorage.setItem(STORE_KEY, JSON.stringify(settings));
    } catch (error) {
      return;
    }
  }

  function clearStored() {
    try {
      window.localStorage.removeItem(STORE_KEY);
    } catch (error) {
      return;
    }
  }

  function resolveTheme(theme) {
    if (theme === 'light') {
      return 'light';
    }

    if (theme === 'auto' && window.matchMedia && window.matchMedia('(prefers-color-scheme: light)').matches) {
      return 'light';
    }

    return 'dark';
  }

  function computeColumns() {
    if (settings.columns > 0) {
      return settings.columns;
    }

    var width = window.innerWidth || 360;
    return Math.max(2, Math.floor(width / settings.padMinWidthPx));
  }

  function applyAccent() {
    if (!accentStyle) {
      accentStyle = document.createElement('style');
      document.head.appendChild(accentStyle);
    }

    accentStyle.textContent =
      '.volume input::-webkit-slider-thumb{background:' + settings.accentColor + ';}' +
      '.row-range input::-webkit-slider-thumb{background:' + settings.accentColor + ';}';
  }

  function apply() {
    dom.root.dataset.theme = resolveTheme(settings.theme);

    applyAccent();

    dom.stopAll.hidden = !settings.showStopAll;
    dom.volumeWrap.hidden = !settings.showMasterVolume;
    dom.footer.hidden = !settings.showStopAll && !settings.showMasterVolume;

    columnCount = computeColumns();

    buildPads();

    if (settings.keepAwake) {
      requestWakeLock();
    }
  }

  function commit() {
    save();
    apply();
  }

  function buildPanel() {
    var index;

    clearNode(dom.settingsBody);

    for (index = 0; index < SCHEMA.length; index++) {
      dom.settingsBody.appendChild(buildRow(SCHEMA[index]));
    }
  }

  function buildRow(spec) {
    var row = document.createElement('div');
    row.className = 'row';

    if (spec.kind === 'check') {
      return buildCheckRow(row, spec);
    }

    var label = document.createElement('span');
    label.className = 'row-label';
    label.textContent = spec.label;
    row.appendChild(label);

    if (spec.kind === 'select') {
      row.appendChild(buildSelect(spec));
    } else if (spec.kind === 'accent') {
      row.appendChild(buildSwatches());
    } else {
      row.appendChild(buildRange(spec));
    }

    return row;
  }

  function buildCheckRow(row, spec) {
    row.className = 'row row-check';

    var input = document.createElement('input');
    input.type = 'checkbox';
    input.checked = settings[spec.key] !== false;

    input.addEventListener('change', function () {
      settings[spec.key] = input.checked;
      commit();
    });

    var label = document.createElement('span');
    label.className = 'row-check-label';
    label.textContent = spec.label;

    addPress(label, function () {
      input.checked = !input.checked;
      settings[spec.key] = input.checked;
      commit();
    });

    row.appendChild(input);
    row.appendChild(label);

    return row;
  }

  function buildSelect(spec) {
    var select = document.createElement('select');
    var index;
    var option;

    for (index = 0; index < spec.options.length; index++) {
      option = document.createElement('option');
      option.value = spec.options[index][0];
      option.textContent = spec.options[index][1];
      select.appendChild(option);
    }

    select.value = String(settings[spec.key]);

    select.addEventListener('change', function () {
      settings[spec.key] = spec.numeric ? Number(select.value) : select.value;
      commit();
    });

    return select;
  }

  function buildRange(spec) {
    var wrap = document.createElement('div');
    wrap.className = 'row-range';

    var input = document.createElement('input');
    input.type = 'range';
    input.min = String(spec.min);
    input.max = String(spec.max);
    input.step = String(spec.step);
    input.value = String(settings[spec.key]);

    var value = document.createElement('output');
    value.className = 'row-value';
    value.textContent = input.value + spec.suffix;

    input.addEventListener('input', function () {
      settings[spec.key] = Number(input.value);
      value.textContent = input.value + spec.suffix;
      commit();
    });

    wrap.appendChild(input);
    wrap.appendChild(value);

    return wrap;
  }

  function buildSwatches() {
    var wrap = document.createElement('div');
    wrap.className = 'swatches';
    var index;

    for (index = 0; index < ACCENTS.length; index++) {
      wrap.appendChild(buildSwatch(ACCENTS[index]));
    }

    return wrap;
  }

  function buildSwatch(colour) {
    var button = document.createElement('button');
    button.type = 'button';
    button.className = 'swatch';
    button.style.background = colour;
    button.setAttribute('data-colour', colour);

    if (settings.accentColor === colour) {
      button.className = 'swatch is-on';
    }

    addPress(button, function () {
      settings.accentColor = colour;
      markSwatches();
      commit();
    });

    return button;
  }

  function markSwatches() {
    var found = dom.settingsBody.getElementsByClassName('swatch');
    var index;

    for (index = 0; index < found.length; index++) {
      var on = found[index].getAttribute('data-colour') === settings.accentColor;
      found[index].className = on ? 'swatch is-on' : 'swatch';
    }
  }

  function send(message) {
    if (!socket || socket.readyState !== WebSocket.OPEN) {
      return;
    }

    socket.send(JSON.stringify(message));
  }

  function addPress(element, handler) {
    element.addEventListener(isTouch ? 'touchstart' : 'mousedown', function (event) {
      event.preventDefault();
      handler();
    });
  }

  function connect() {
    var scheme = window.location.protocol === 'https:' ? 'wss' : 'ws';
    socket = new WebSocket(scheme + '://' + window.location.host + '/ws');

    socket.addEventListener('open', function () {
      retryDelay = 500;
    });

    socket.addEventListener('message', function (event) {
      var message;

      try {
        message = JSON.parse(event.data);
      } catch (error) {
        return;
      }

      handle(message);
    });

    socket.addEventListener('close', function () {
      pads.forEach(function (pad) {
        pad.classList.remove('is-active');
      });

      window.setTimeout(connect, retryDelay);
      retryDelay = Math.min(retryDelay * 2, 5000);
    });

    socket.addEventListener('error', function () {
      socket.close();
    });
  }

  function handle(message) {
    if (message.type === 'hello') {
      applyHello(message);
    } else if (message.type === 'state') {
      applyState(message);
    } else if (message.type === 'error') {
      console.warn('YASoundBoard:', message.message);
    }
  }

  function applyHello(message) {
    sounds = message.sounds || [];

    document.title = message.title;

    load();
    buildPanel();
    apply();

    if (message.audio) {
      syncVolume(message.audio.masterGain);
    }
  }

  function applyState(message) {
    var active = new Set(message.active || []);

    pads.forEach(function (pad, id) {
      var on = active.has(id);
      pad.classList.toggle('is-active', on);
      pad.style.borderColor = on ? pad.getAttribute('data-accent') : '';
    });

    if (!volumeDragging) {
      syncVolume(message.masterGain);
    }
  }

  function buildPads() {
    clearNode(dom.grid);
    pads.clear();

    if (sounds.length === 0) {
      dom.empty.hidden = false;
      return;
    }

    dom.empty.hidden = true;

    var fragment = document.createDocumentFragment();
    var index;

    for (index = 0; index < sounds.length; index++) {
      fragment.appendChild(buildPad(sounds[index]));
    }

    dom.grid.appendChild(fragment);
  }

  function buildPad(sound) {
    var accent = sound.color || settings.accentColor;
    var pad = document.createElement('button');

    pad.type = 'button';
    pad.className = 'pad';
    pad.dataset.id = sound.id;
    pad.setAttribute('data-accent', accent);
    pad.style.width = 'calc(' + (100 / columnCount) + '% - ' + settings.padGapPx + 'px)';
    pad.style.margin = (settings.padGapPx / 2) + 'px';
    pad.style.borderRadius = settings.padRadiusPx + 'px';

    var fill = document.createElement('span');
    fill.className = 'pad-fill';
    fill.style.background = accent;
    pad.appendChild(fill);

    if (settings.showLabels) {
      var label = document.createElement('span');
      label.className = 'pad-label';
      label.textContent = sound.name;
      pad.appendChild(label);
    }

    addPress(pad, function () {
      trigger(sound.id);
    });

    pad.addEventListener('contextmenu', function (event) {
      event.preventDefault();
    });

    pads.set(sound.id, pad);

    return pad;
  }

  function trigger(id) {
    send({ type: 'play', id: id });

    var pad = pads.get(id);
    if (!pad) {
      return;
    }

    if (settings.flashOnTap) {
      pad.classList.remove('is-hit');
      void pad.offsetWidth;
      pad.classList.add('is-hit');
      window.setTimeout(function () {
        pad.classList.remove('is-hit');
      }, 150);
    }

    if (settings.vibrateOnTap && navigator.vibrate) {
      navigator.vibrate(settings.vibrateMs);
    }
  }

  function syncVolume(gain) {
    if (typeof gain !== 'number') {
      return;
    }

    var percent = Math.round(gain * 100);
    dom.volume.value = String(percent);
    dom.volumeValue.textContent = percent + '%';
  }

  function requestWakeLock() {
    if (!('wakeLock' in navigator) || wakeLock) {
      return;
    }

    navigator.wakeLock.request('screen').then(function (lock) {
      wakeLock = lock;
      wakeLock.addEventListener('release', function () {
        wakeLock = null;
      });
    }, function () {
      wakeLock = null;
    });
  }

  dom.settingsOpen.addEventListener('click', function () {
    dom.panel.hidden = false;
  });

  dom.settingsClose.addEventListener('click', function () {
    dom.panel.hidden = true;
  });

  dom.settingsReset.addEventListener('click', function () {
    clearStored();
    load();
    buildPanel();
    apply();
  });

  dom.volume.addEventListener('input', function () {
    volumeDragging = true;

    var percent = Number(dom.volume.value);
    dom.volumeValue.textContent = percent + '%';
    send({ type: 'setMasterGain', value: percent / 100 });
  });

  dom.volume.addEventListener('change', function () {
    volumeDragging = false;
  });

  dom.stopAll.addEventListener('click', function () {
    send({ type: 'stopAll' });
  });

  document.addEventListener('visibilitychange', function () {
    if (document.visibilityState === 'visible') {
      requestWakeLock();
    }
  });

  window.addEventListener('resize', function () {
    var next = computeColumns();

    if (next !== columnCount) {
      columnCount = next;
      buildPads();
    }
  });

  connect();
})();
