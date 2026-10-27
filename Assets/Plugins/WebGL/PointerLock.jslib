// Browsers only allow pointer lock inside a real user gesture. Unity's Cursor.lockState
// requests it from the game loop, which Safari rejects with an uncaught NotAllowedError
// (fatal for the Unity page). Here the lock is requested from the canvas click itself,
// and any rejection is swallowed.
mergeInto(LibraryManager.library, {

  WebPointerLock_SetWanted: function (wanted) {
    var canvas = Module["canvas"];
    if (!canvas) return;

    if (!window.__pointerLock) {
      window.__pointerLock = { wanted: false };
      var tryLock = function () {
        if (!window.__pointerLock.wanted || document.pointerLockElement === canvas) return;
        try {
          var result = canvas.requestPointerLock();
          if (result && typeof result.catch === "function") result.catch(function () {});
        } catch (e) { /* not allowed right now: the next click will try again */ }
      };
      canvas.addEventListener("mousedown", tryLock);
      canvas.addEventListener("click", tryLock);
    }

    window.__pointerLock.wanted = !!wanted;

    if (!wanted && document.pointerLockElement === canvas) {
      try { document.exitPointerLock(); } catch (e) { }
    }
  },

  WebPointerLock_IsLocked: function () {
    return document.pointerLockElement === Module["canvas"] ? 1 : 0;
  }
});
