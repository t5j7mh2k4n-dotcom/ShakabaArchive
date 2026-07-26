(function () {
    var profiles = {
        screen: { width: 3, height: 90, margin: 16, compactWidth: 2, compactHeight: 40, compactMargin: 10 },
        print: { width: 4, height: 110, margin: 24, compactWidth: 3, compactHeight: 50, compactMargin: 12 }
    };

    function renderBarcodes(mode) {
        if (typeof JsBarcode === "undefined") {
            return;
        }

        var profile = profiles[mode] || profiles.screen;
        var isPrint = mode === "print";

        document.querySelectorAll(".person-barcode[data-barcode]").forEach(function (canvas) {
            var value = canvas.getAttribute("data-barcode") || "";
            if (!value) {
                return;
            }

            var compact = canvas.getAttribute("data-compact") === "1";
            var renderedKey = isPrint ? "data-print-rendered" : "data-rendered";
            if (!isPrint && canvas.getAttribute(renderedKey) === "1") {
                return;
            }

            try {
                JsBarcode(canvas, value, {
                    format: "CODE128",
                    width: compact ? profile.compactWidth : profile.width,
                    height: compact ? profile.compactHeight : profile.height,
                    displayValue: false,
                    margin: compact ? profile.compactMargin : profile.margin,
                    background: "#ffffff",
                    lineColor: "#000000"
                });
                canvas.setAttribute(renderedKey, "1");
                if (!isPrint) {
                    canvas.setAttribute("data-rendered", "1");
                }
            } catch (err) {
                console.warn("Barcode render failed:", value, err);
            }
        });
    }

    function renderQrCodes(mode) {
        if (typeof QRCode === "undefined") {
            return;
        }

        var size = mode === "print" ? 240 : 200;
        var isPrint = mode === "print";

        document.querySelectorAll(".person-qr[data-qr-url]").forEach(function (host) {
            var url = host.getAttribute("data-qr-url") || "";
            if (!url) {
                return;
            }

            var renderedKey = isPrint ? "data-print-rendered" : "data-rendered";
            if (!isPrint && host.getAttribute(renderedKey) === "1") {
                return;
            }

            host.innerHTML = "";
            new QRCode(host, {
                text: url,
                width: size,
                height: size,
                colorDark: "#000000",
                colorLight: "#ffffff",
                correctLevel: QRCode.CorrectLevel.H
            });
            host.setAttribute(renderedKey, "1");
            if (!isPrint) {
                host.setAttribute("data-rendered", "1");
            }
        });
    }

    function bindPrintButton() {
        var btn = document.getElementById("btn-print-person-codes");
        if (!btn || btn.getAttribute("data-bound") === "1") {
            return;
        }

        btn.setAttribute("data-bound", "1");
        btn.addEventListener("click", function () {
            renderBarcodes("print");
            renderQrCodes("print");
            document.body.classList.add("printing-person-codes");
            window.setTimeout(function () {
                window.print();
            }, 250);
        });
    }

    function bindPrintEvents() {
        window.addEventListener("beforeprint", function () {
            renderBarcodes("print");
            renderQrCodes("print");
            document.body.classList.add("printing-person-codes");
        });

        window.addEventListener("afterprint", function () {
            document.body.classList.remove("printing-person-codes");
        });
    }

    function init() {
        renderBarcodes("screen");
        renderQrCodes("screen");
        bindPrintButton();
        bindPrintEvents();
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", init);
    } else {
        init();
    }
})();
