(function () {
    function renderBarcodes() {
        if (typeof JsBarcode === "undefined") {
            return;
        }

        document.querySelectorAll(".person-barcode[data-barcode]").forEach(function (svg) {
            if (svg.getAttribute("data-rendered") === "1") {
                return;
            }

            var value = svg.getAttribute("data-barcode") || "";
            if (!value) {
                return;
            }

            var compact = svg.getAttribute("data-compact") === "1";
            try {
                JsBarcode(svg, value, {
                    format: "CODE128",
                    width: compact ? 1 : 2,
                    height: compact ? 28 : 56,
                    displayValue: false,
                    margin: compact ? 2 : 8,
                    background: "#fffcf7",
                    lineColor: "#1f2924"
                });
                svg.setAttribute("data-rendered", "1");
            } catch (err) {
                console.warn("Barcode render failed:", value, err);
            }
        });
    }

    function renderQrCodes() {
        if (typeof QRCode === "undefined") {
            return;
        }

        document.querySelectorAll(".person-qr[data-qr-url]").forEach(function (host) {
            if (host.getAttribute("data-rendered") === "1") {
                return;
            }

            var url = host.getAttribute("data-qr-url") || "";
            if (!url) {
                return;
            }

            host.innerHTML = "";
            new QRCode(host, {
                text: url,
                width: 160,
                height: 160,
                colorDark: "#1f2924",
                colorLight: "#fffcf7",
                correctLevel: QRCode.CorrectLevel.M
            });
            host.setAttribute("data-rendered", "1");
        });
    }

    function bindPrintButton() {
        var btn = document.getElementById("btn-print-person-codes");
        if (!btn || btn.getAttribute("data-bound") === "1") {
            return;
        }

        btn.setAttribute("data-bound", "1");
        btn.addEventListener("click", function () {
            document.body.classList.add("printing-person-codes");
            window.print();
            window.setTimeout(function () {
                document.body.classList.remove("printing-person-codes");
            }, 500);
        });
    }

    function init() {
        renderBarcodes();
        renderQrCodes();
        bindPrintButton();
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", init);
    } else {
        init();
    }
})();
