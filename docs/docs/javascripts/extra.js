document$.subscribe(function () {
  document.querySelectorAll("article a[href]").forEach(function (link) {
    if (link.hostname && link.hostname !== location.hostname) {
      link.setAttribute("target", "_blank");
      link.setAttribute("rel", "noopener noreferrer");
    }
  });
});
