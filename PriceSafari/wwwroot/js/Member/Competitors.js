document.addEventListener('DOMContentLoaded', function () {
    const searchInput = document.getElementById('storeSearch');
    const competitorTableRows = document.querySelectorAll('#competitors-table tbody tr');
    const scrapableCountSpan = document.getElementById('scrapable-count');
    const tooltip = document.getElementById('barTooltip');

    searchInput.addEventListener('input', function () {
        const searchTerm = searchInput.value.toLowerCase();
        let visibleCount = 0;

        competitorTableRows.forEach(row => {
            const competitorName = row.querySelector('.competitor-name').textContent.toLowerCase();
            if (competitorName.includes(searchTerm)) {
                row.style.display = '';
                visibleCount++;
            } else {
                row.style.display = 'none';
            }
        });

        scrapableCountSpan.textContent = visibleCount;
    });

    scrapableCountSpan.textContent = competitorTableRows.length;

    competitorTableRows.forEach(row => {
        row.addEventListener('click', function (e) {

            if (e.target.closest('.competitorsBarBox')) return;
            const url = row.getAttribute('data-url');
            if (url) window.location.href = url;
        });
    });

    const allBars = document.querySelectorAll('.competitorsBarBox');

    allBars.forEach(bar => {
        const lower = parseInt(bar.dataset.lower) || 0;
        const same = parseInt(bar.dataset.same) || 0;
        const higher = parseInt(bar.dataset.higher) || 0;
        const total = parseInt(bar.dataset.total) || 0;

        const tooltipHtml = `
            <div class="tt-row">
                <span class="tt-dot" style="background: rgba(0, 145, 123, 0.85);"></span>
                <span>Masz niższą cenę</span>
                <span class="tt-value">${lower} (${total > 0 ? ((lower / total) * 100).toFixed(1) : 0}%)</span>
            </div>
            <div class="tt-row">
                <span class="tt-dot" style="background: rgba(117, 152, 112, 0.85);"></span>
                <span>Taka sama cena</span>
                <span class="tt-value">${same} (${total > 0 ? ((same / total) * 100).toFixed(1) : 0}%)</span>
            </div>
            <div class="tt-row">
                <span class="tt-dot" style="background: rgba(171, 37, 32, 0.85);"></span>
                <span>Masz wyższą cenę</span>
                <span class="tt-value">${higher} (${total > 0 ? ((higher / total) * 100).toFixed(1) : 0}%)</span>
            </div>`;

        bar.addEventListener('mouseenter', function () {
            tooltip.innerHTML = tooltipHtml;
            tooltip.style.display = 'block';
        });

        bar.addEventListener('mousemove', function (e) {
            const offsetX = 14;
            const offsetY = 14;
            const ttWidth = tooltip.offsetWidth;
            const ttHeight = tooltip.offsetHeight;

            let x = e.clientX + offsetX;
            let y = e.clientY + offsetY;

            if (x + ttWidth > window.innerWidth - 8) {
                x = e.clientX - ttWidth - offsetX;
            }
            if (y + ttHeight > window.innerHeight - 8) {
                y = e.clientY - ttHeight - offsetX;
            }

            tooltip.style.left = x + 'px';
            tooltip.style.top = y + 'px';
        });

        bar.addEventListener('mouseleave', function () {
            tooltip.style.display = 'none';
        });

        bar.addEventListener('click', function () {
            const row = bar.closest('tr');
            if (row) {
                const url = row.getAttribute('data-url');
                if (url) window.location.href = url;
            }
        });

        bar.style.cursor = 'pointer';
    });
});