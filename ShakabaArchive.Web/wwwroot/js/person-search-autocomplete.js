(function () {
    var input = document.getElementById("people-search-q");
    var fieldSelect = document.getElementById("people-search-field");
    var list = document.getElementById("people-search-suggestions");
    if (!input || !fieldSelect || !list) {
        return;
    }

    var timer = null;
    var activeIndex = -1;
    var currentItems = [];

    function hideList() {
        list.hidden = true;
        list.innerHTML = "";
        activeIndex = -1;
        currentItems = [];
    }

    function showList() {
        if (currentItems.length > 0) {
            list.hidden = false;
        }
    }

    function renderItems(items) {
        currentItems = items || [];
        list.innerHTML = "";

        if (currentItems.length === 0) {
            hideList();
            return;
        }

        currentItems.forEach(function (item, index) {
            var btn = document.createElement("button");
            btn.type = "button";
            btn.className = "person-search-suggestion-item";
            btn.setAttribute("data-index", String(index));
            btn.innerHTML = "<span class=\"person-search-suggestion-value\">" + escapeHtml(item.value) + "</span>" +
                (item.label && item.label !== item.value
                    ? "<span class=\"person-search-suggestion-label\">" + escapeHtml(item.label) + "</span>"
                    : "");
            btn.addEventListener("mousedown", function (e) {
                e.preventDefault();
                selectItem(index);
            });
            list.appendChild(btn);
        });

        showList();
    }

    function escapeHtml(text) {
        return String(text)
            .replace(/&/g, "&amp;")
            .replace(/</g, "&lt;")
            .replace(/>/g, "&gt;")
            .replace(/"/g, "&quot;");
    }

    function selectItem(index) {
        var item = currentItems[index];
        if (!item) {
            return;
        }
        input.value = item.value;
        hideList();
    }

    function highlightItem(index) {
        var buttons = list.querySelectorAll(".person-search-suggestion-item");
        buttons.forEach(function (btn, i) {
            btn.classList.toggle("is-active", i === index);
        });
        activeIndex = index;
    }

    function fetchSuggestions() {
        var q = input.value.trim();
        if (q.length < 1) {
            hideList();
            return;
        }

        var url = new URL(window.location.pathname, window.location.origin);
        url.searchParams.set("handler", "Suggest");
        url.searchParams.set("field", fieldSelect.value || "all");
        url.searchParams.set("q", q);

        fetch(url.toString(), {
            headers: { "Accept": "application/json" }
        })
            .then(function (res) { return res.ok ? res.json() : []; })
            .then(renderItems)
            .catch(function () { hideList(); });
    }

    function scheduleFetch() {
        window.clearTimeout(timer);
        timer = window.setTimeout(fetchSuggestions, 220);
    }

    input.addEventListener("input", scheduleFetch);
    input.addEventListener("focus", scheduleFetch);
    fieldSelect.addEventListener("change", scheduleFetch);

    input.addEventListener("keydown", function (e) {
        if (list.hidden || currentItems.length === 0) {
            return;
        }

        if (e.key === "ArrowDown") {
            e.preventDefault();
            highlightItem(Math.min(activeIndex + 1, currentItems.length - 1));
        } else if (e.key === "ArrowUp") {
            e.preventDefault();
            highlightItem(Math.max(activeIndex - 1, 0));
        } else if (e.key === "Enter" && activeIndex >= 0) {
            e.preventDefault();
            selectItem(activeIndex);
        } else if (e.key === "Escape") {
            hideList();
        }
    });

    document.addEventListener("click", function (e) {
        if (!list.contains(e.target) && e.target !== input) {
            hideList();
        }
    });
})();
