(function () {
  'use strict';

  var dom = {
    root: document.documentElement,
    grid: document.getElementById('pads'),
    empty: document.getElementById('empty'),
    footer: document.getElementById('footer'),
    stopAll: document.getElementById('stopAll'),
    volumeWrap: document.getElementById('volumeWrap'),
    volume: document.getElementById('masterVolume'),
    volumeValue: document.getElementById('volumeValue'),
  };

  var pads = new Map();
  var isTouch = 'ontouchstart' in window;

  var socket = null;
  var retryDelay = 500;
  var wakeLock = null;
  var settings = null;
  var volumeDragging = false;

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

    applyTheme(settings.theme);

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
    var active = new Set(message.active || []);

    pads.forEach(function (pad, id) {
      pad.classList.toggle('is-active', active.has(id));
    });

    if (!volumeDragging) {
      syncVolume(message.masterGain);
    }
  }

  function clearGrid() {
    while (dom.grid.firstChild) {
      dom.grid.removeChild(dom.grid.firstChild);
    }
  }

  function buildPads(sounds) {
    clearGrid();
    pads.clear();

    if (!sounds || sounds.length === 0) {
      dom.empty.hidden = false;
      return;
    }

    dom.empty.hidden = true;

    var fragment = document.createDocumentFragment();

    sounds.forEach(function (sound) {
      var pad = document.createElement('button');
      pad.type = 'button';
      pad.className = 'pad';
      pad.dataset.id = sound.id;

      if (sound.color) {
        pad.style.setProperty('--pad-accent', sound.color);
      }

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

      fragment.appendChild(pad);
      pads.set(sound.id, pad);
    });

    dom.grid.appendChild(fragment);
  }

  function trigger(id) {
    send({ type: 'play', id: id });

    var pad = pads.get(id);
    if (!pad) {
      return;
    }

    if (settings && settings.flashOnTap) {
      pad.classList.remove('is-hit');
      void pad.offsetWidth;
      pad.classList.add('is-hit');
      window.setTimeout(function () {
        pad.classList.remove('is-hit');
      }, 150);
    }

    if (settings && settings.vibrateOnTap && navigator.vibrate) {
      navigator.vibrate(settings.vibrateMs || 15);
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

  function applyTheme(theme) {
    var resolved = 'dark';

    if (theme === 'auto') {
      resolved = window.matchMedia('(prefers-color-scheme: light)').matches ? 'light' : 'dark';
    } else if (theme === 'light') {
      resolved = 'light';
    }

    dom.root.dataset.theme = resolved;
  }

  function requestWakeLock() {
    if (!settings || !settings.keepAwake) {
      return;
    }

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

  connect();
})();
