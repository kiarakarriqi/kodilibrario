/* ─────────────────────────────────────────────────────────────
   Custom dialog system v2 — branded, animated, with icons
   Usage:
     var ok = await customConfirm('Delete this item?');
     var ok = await customConfirm('Delete?', { danger: true });
     var name = await customPrompt('Enter your name:');
   ───────────────────────────────────────────────────────────── */
(function() {
  if (document.getElementById('custom-dialog-styles')) {
    document.getElementById('custom-dialog-styles').remove();
  }
  if (document.getElementById('custom-dialog-overlay')) {
    document.getElementById('custom-dialog-overlay').remove();
  }

  var style = document.createElement('style');
  style.id = 'custom-dialog-styles';
  style.textContent = [
    '#cd-overlay{display:none;position:fixed;inset:0;background:rgba(15,40,30,0.55);backdrop-filter:blur(3px);-webkit-backdrop-filter:blur(3px);z-index:10000;align-items:center;justify-content:center;font-family:inherit;animation:cd-fade 0.18s ease-out}',
    '#cd-overlay.show{display:flex}',
    '@keyframes cd-fade{from{opacity:0}to{opacity:1}}',
    '@keyframes cd-pop{from{transform:translateY(12px) scale(0.96);opacity:0}to{transform:translateY(0) scale(1);opacity:1}}',
    '#cd-box{background:#ffffff;border-radius:14px;max-width:460px;width:90%;box-shadow:0 20px 50px rgba(0,0,0,0.25),0 6px 18px rgba(26,92,66,0.15);animation:cd-pop 0.22s cubic-bezier(0.34,1.56,0.64,1);overflow:hidden}',
    '.cd-header{display:flex;align-items:center;gap:12px;padding:18px 22px;background:linear-gradient(135deg,#1a5c42 0%,#2d8659 100%);color:white}',
    '.cd-header.danger{background:linear-gradient(135deg,#a8311f 0%,#e74c3c 100%)}',
    '.cd-header.input{background:linear-gradient(135deg,#155236 0%,#1a5c42 100%)}',
    '.cd-icon{flex-shrink:0;width:28px;height:28px;display:flex;align-items:center;justify-content:center;background:rgba(255,255,255,0.2);border-radius:50%;font-size:16px}',
    '.cd-title{font-size:16px;font-weight:600;margin:0;letter-spacing:0.2px}',
    '.cd-body{padding:22px}',
    '.cd-message{font-size:14px;color:#333;line-height:1.55;margin:0 0 16px 0;white-space:pre-line}',
    '.cd-message:last-child{margin-bottom:0}',
    '.cd-input{width:100%;padding:11px 14px;border:1.5px solid #d4ddd6;border-radius:8px;font-size:14px;font-family:inherit;transition:border-color 0.15s,box-shadow 0.15s;box-sizing:border-box;background:#fafbfa}',
    '.cd-input:focus{outline:none;border-color:#1a5c42;box-shadow:0 0 0 3px rgba(26,92,66,0.15);background:white}',
    '.cd-buttons{display:flex;gap:10px;justify-content:flex-end;padding:14px 22px 20px 22px;background:#f6f9f7;border-top:1px solid #e8eee9}',
    '.cd-btn{padding:10px 22px;border:none;border-radius:8px;cursor:pointer;font-weight:600;font-size:14px;font-family:inherit;transition:all 0.15s;letter-spacing:0.2px;position:relative;overflow:hidden}',
    '.cd-btn:active{transform:translateY(1px)}',
    '.cd-btn-cancel{background:white;color:#555;border:1.5px solid #d4ddd6}',
    '.cd-btn-cancel:hover{background:#f0f3f1;border-color:#b8c5bd;color:#333}',
    '.cd-btn-confirm{background:linear-gradient(135deg,#1a5c42 0%,#2d8659 100%);color:white;box-shadow:0 2px 8px rgba(26,92,66,0.3)}',
    '.cd-btn-confirm:hover{box-shadow:0 4px 14px rgba(26,92,66,0.4);transform:translateY(-1px)}',
    '.cd-btn-danger{background:linear-gradient(135deg,#a8311f 0%,#e74c3c 100%);color:white;box-shadow:0 2px 8px rgba(231,76,60,0.3)}',
    '.cd-btn-danger:hover{box-shadow:0 4px 14px rgba(231,76,60,0.4);transform:translateY(-1px)}'
  ].join('');
  document.head.appendChild(style);

  // Icons as inline SVG
  var ICONS = {
    confirm: '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="white" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><path d="M9 12l2 2 4-4"/><circle cx="12" cy="12" r="10"/></svg>',
    danger:  '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="white" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><path d="M10.29 3.86L1.82 18a2 2 0 001.71 3h16.94a2 2 0 001.71-3L13.71 3.86a2 2 0 00-3.42 0z"/><line x1="12" y1="9" x2="12" y2="13"/><line x1="12" y1="17" x2="12.01" y2="17"/></svg>',
    input:   '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="white" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><path d="M11 4H4a2 2 0 00-2 2v14a2 2 0 002 2h14a2 2 0 002-2v-7"/><path d="M18.5 2.5a2.121 2.121 0 013 3L12 15l-4 1 1-4 9.5-9.5z"/></svg>'
  };

  var overlay = document.createElement('div');
  overlay.id = 'cd-overlay';
  overlay.innerHTML =
    '<div id="cd-box" role="dialog" aria-modal="true">' +
      '<div class="cd-header" id="cd-header">' +
        '<div class="cd-icon" id="cd-icon"></div>' +
        '<h3 class="cd-title" id="cd-title">Confirm</h3>' +
      '</div>' +
      '<div class="cd-body">' +
        '<p class="cd-message" id="cd-message"></p>' +
        '<input id="cd-input" class="cd-input" type="text" style="display:none" />' +
      '</div>' +
      '<div class="cd-buttons" id="cd-buttons"></div>' +
    '</div>';
  document.body.appendChild(overlay);

  function show(opts) {
    return new Promise(function(resolve) {
      var ov     = document.getElementById('cd-overlay');
      var hdr    = document.getElementById('cd-header');
      var iconEl = document.getElementById('cd-icon');
      var ttl    = document.getElementById('cd-title');
      var msg    = document.getElementById('cd-message');
      var inp    = document.getElementById('cd-input');
      var btns   = document.getElementById('cd-buttons');

      // Header style + icon
      hdr.className = 'cd-header';
      if (opts.danger) hdr.className += ' danger';
      else if (opts.type === 'prompt') hdr.className += ' input';
      iconEl.innerHTML = opts.danger ? ICONS.danger : (opts.type === 'prompt' ? ICONS.input : ICONS.confirm);

      ttl.textContent = opts.title || (opts.type === 'prompt' ? 'Input required' : (opts.danger ? 'Warning' : 'Confirm'));
      msg.textContent = opts.message || '';
      btns.innerHTML  = '';

      if (opts.type === 'prompt') {
        inp.style.display = 'block';
        inp.value         = opts.defaultValue || '';
        inp.placeholder   = opts.placeholder || '';
      } else {
        inp.style.display = 'none';
      }

      function cleanup(result) {
        ov.classList.remove('show');
        document.removeEventListener('keydown', onKey);
        resolve(result);
      }
      function onKey(e) {
        if (e.key === 'Escape') cleanup(opts.type === 'prompt' ? null : false);
        if (e.key === 'Enter' && opts.type === 'prompt') { e.preventDefault(); cleanup(inp.value); }
      }

      var cancelBtn = document.createElement('button');
      cancelBtn.className = 'cd-btn cd-btn-cancel';
      cancelBtn.textContent = opts.cancelText || 'Cancel';
      cancelBtn.onclick = function() { cleanup(opts.type === 'prompt' ? null : false); };

      var okBtn = document.createElement('button');
      okBtn.className = 'cd-btn ' + (opts.danger ? 'cd-btn-danger' : 'cd-btn-confirm');
      okBtn.textContent = opts.okText || 'OK';
      okBtn.onclick = function() { cleanup(opts.type === 'prompt' ? inp.value : true); };

      btns.appendChild(cancelBtn);
      btns.appendChild(okBtn);

      ov.classList.add('show');
      document.addEventListener('keydown', onKey);

      setTimeout(function() {
        if (opts.type === 'prompt') inp.focus();
        else okBtn.focus();
      }, 80);
    });
  }

  window.customConfirm = function(message, options) {
    options = options || {};
    return show({
      type: 'confirm',
      title: options.title,
      message: message,
      okText: options.okText,
      cancelText: options.cancelText,
      danger: options.danger || false
    });
  };

  window.customPrompt = function(message, defaultValue, options) {
    options = options || {};
    return show({
      type: 'prompt',
      title: options.title,
      message: message,
      defaultValue: defaultValue || '',
      placeholder: options.placeholder || '',
      okText: options.okText,
      cancelText: options.cancelText
    });
  };
})();
