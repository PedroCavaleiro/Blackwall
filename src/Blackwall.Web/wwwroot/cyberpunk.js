(function () {
    "use strict";

    var STORAGE_KEY = "bw_boot_shown";
    var BOOT_DURATION = 1600;

    function prefersReducedMotion() {
        return window.matchMedia && window.matchMedia("(prefers-reduced-motion: reduce)").matches;
    }

    function shouldShowBoot() {
        if (prefersReducedMotion()) return false;
        try {
            return sessionStorage.getItem(STORAGE_KEY) !== "1";
        } catch (e) {
            return true;
        }
    }

    function markBootShown() {
        try {
            sessionStorage.setItem(STORAGE_KEY, "1");
        } catch (e) { /* ignore */ }
    }

    function createOverlay() {
        var overlay = document.createElement("div");
        overlay.className = "bw-boot-overlay";

        var content = document.createElement("div");
        content.className = "bw-boot-content";

        var lines = [
            "> BLACKWALL.SYS v1.0.0",
            "> Initializing barrier protocols...",
            "> Loading spam detection modules... OK",
            "> Calibrating raid detection... OK",
            "> BARRIER ONLINE. WELCOME, OPERATOR."
        ];

        lines.forEach(function (text) {
            var line = document.createElement("div");
            line.className = "bw-boot-line";
            line.textContent = text;
            content.appendChild(line);
        });

        var bar = document.createElement("div");
        bar.className = "bw-boot-bar";
        var barFill = document.createElement("div");
        barFill.className = "bw-boot-bar-fill";
        bar.appendChild(barFill);
        content.appendChild(bar);

        overlay.appendChild(content);
        document.body.appendChild(overlay);

        setTimeout(function () {
            overlay.classList.add("bw-boot-done");
            setTimeout(function () {
                if (overlay.parentNode) overlay.parentNode.removeChild(overlay);
            }, 600);
        }, BOOT_DURATION);
    }

    function init() {
        if (shouldShowBoot()) {
            createOverlay();
            markBootShown();
        }
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", init);
    } else {
        init();
    }
})();
