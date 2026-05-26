(function() {
  var API = 'http://localhost:5273/api';
  var pollTimer = null;

  function getUserId() { return parseInt(sessionStorage.getItem('userId'), 10); }

  function shouldShow() {
    var page = window.location.pathname;
    return !page.includes('login.html') && !page.includes('register.html');
  }

  function updateBell() {
    var uid = getUserId();
    if (!uid) return;
    fetch(API + '/notifications/' + uid)
      .then(function(r) { return r.json(); })
      .then(function(data) {
        var unread = data.filter(function(n) { return !n.isRead; }).length;
        var badge  = document.getElementById('notif-count-badge');
        if (!badge) return;
        if (unread > 0) {
          badge.textContent    = unread > 99 ? '99+' : String(unread);
          badge.style.display  = 'inline-flex';
        } else {
          badge.style.display  = 'none';
        }
      }).catch(function() {});
  }

  function init() {
    if (!shouldShow()) return;
    var uid = getUserId();
    if (!uid) return;
    updateBell();
    if (pollTimer) clearInterval(pollTimer);
    pollTimer = setInterval(updateBell, 30000);
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', init);
  } else {
    init();
  }
})();
