document.addEventListener("DOMContentLoaded", function () {
    const EXPORT_HUB_URL = '/scrapingHub';
    const EXPORT_MAX_SCRAPS = 12;

    const exportButton = document.getElementById("exportToExcelButton");

    if (exportButton) {
        let exportAvailableScraps = [];
        let exportSelectedIds = new Set();
        let exportHubConnection = null;
        let exportSelectedType = 'prices';

        const polishMonths = [
            'Styczeń', 'Luty', 'Marzec', 'Kwiecień', 'Maj', 'Czerwiec',
            'Lipiec', 'Sierpień', 'Wrzesień', 'Październik', 'Listopad', 'Grudzień'
        ];
        const polishDaysShort = ['Ndz', 'Pon', 'Wt', 'Śr', 'Czw', 'Pt', 'Sob'];

        exportButton.addEventListener("click", function () {
            showExportStep('select');
            exportSelectedIds.clear();
            exportSelectedType = 'prices';
            updateExportTypeUI();
            updateExportSelectionUI();
            $('#exportScrapSelectorModal').modal('show');
            loadAvailableScraps();
        });

        document.querySelectorAll('.export-type-option').forEach(option => {
            option.addEventListener('click', function () {
                const radio = this.querySelector('input[type="radio"]');
                radio.checked = true;
                exportSelectedType = radio.value;
                updateExportTypeUI();
            });
        });

        function updateExportTypeUI() {
            document.querySelectorAll('.export-type-option').forEach(opt => {
                const radio = opt.querySelector('input[type="radio"]');
                opt.classList.toggle('selected', radio.checked);
            });
        }

        function loadAvailableScraps() {
            const listContainer = document.getElementById('exportScrapList');
            listContainer.innerHTML = `
            <div style="padding: 40px; text-align: center; color: #888;">
                <i class="fa-solid fa-spinner fa-spin" style="font-size: 20px;"></i>
                <p style="margin-top: 10px;">Ładowanie listy analiz...</p>
            </div>`;

            // ZMIANA: Strzał do nowego API
            fetch(`/api/MassExport/GetAvailableScraps?storeId=${storeId}`)
                .then(r => r.json())
                .then(scraps => {
                    exportAvailableScraps = scraps;
                    renderScrapList(scraps);
                })
                .catch(err => {
                    console.error('Błąd ładowania analiz:', err);
                    listContainer.innerHTML = `
                    <div style="padding: 30px; text-align: center; color: #dc3545;">
                        <i class="fa-solid fa-circle-exclamation" style="font-size: 20px;"></i>
                        <p style="margin-top: 8px;">Nie udało się załadować listy analiz.</p>
                    </div>`;
                });
        }

        function renderScrapList(scraps) {
            const listContainer = document.getElementById('exportScrapList');

            if (!scraps || scraps.length === 0) {
                listContainer.innerHTML = `
                <div style="padding: 30px; text-align: center; color: #888;">
                    Brak dostępnych analiz dla tego sklepu.
                </div>`;
                return;
            }

            let html = '';
            let currentMonthKey = '';

            scraps.forEach((scrap, idx) => {
                const date = new Date(scrap.date);
                const monthKey = `${date.getFullYear()}-${date.getMonth()}`;

                if (monthKey !== currentMonthKey) {
                    currentMonthKey = monthKey;
                    html += `<div class="export-scrap-month-header">${polishMonths[date.getMonth()]} ${date.getFullYear()}</div>`;
                }

                const dayStr = date.getDate().toString().padStart(2, '0') + '.' +
                    (date.getMonth() + 1).toString().padStart(2, '0') + '.' +
                    date.getFullYear();
                const dayName = polishDaysShort[date.getDay()];
                const timeStr = date.getHours().toString().padStart(2, '0') + ':' +
                    date.getMinutes().toString().padStart(2, '0');
                const priceCountFormatted = scrap.priceCount.toLocaleString('pl-PL');
                const isSelected = exportSelectedIds.has(scrap.id);

                html += `
                <div class="export-scrap-row ${isSelected ? 'selected' : ''}"
                     data-scrap-id="${scrap.id}" data-idx="${idx}">
                    <input type="checkbox" ${isSelected ? 'checked' : ''} />
                    <span class="export-scrap-date">${dayStr}</span>
                    <span class="export-scrap-day">${dayName}</span>
                    <span class="export-scrap-time">${timeStr}</span>
                    <span class="export-scrap-prices">${priceCountFormatted} cen</span>
                </div>`;
            });

            listContainer.innerHTML = html;

            listContainer.querySelectorAll('.export-scrap-row').forEach(row => {
                row.addEventListener('click', function (e) {
                    if (e.target.tagName === 'INPUT') return;
                    toggleScrapRow(this);
                });

                row.querySelector('input[type="checkbox"]').addEventListener('click', function (e) {
                    e.stopPropagation();
                    toggleScrapCheckbox(this, row);
                });
            });
        }

        function toggleScrapRow(row) {
            const checkbox = row.querySelector('input[type="checkbox"]');
            const scrapId = parseInt(row.dataset.scrapId);

            if (checkbox.checked) {
                checkbox.checked = false;
                exportSelectedIds.delete(scrapId);
                row.classList.remove('selected');
            } else {
                if (exportSelectedIds.size >= EXPORT_MAX_SCRAPS) {
                    showGlobalNotification(`<p>Maksymalnie ${EXPORT_MAX_SCRAPS} analiz.</p>`);
                    return;
                }
                checkbox.checked = true;
                exportSelectedIds.add(scrapId);
                row.classList.add('selected');
            }
            updateExportSelectionUI();
        }

        function toggleScrapCheckbox(checkbox, row) {
            const scrapId = parseInt(row.dataset.scrapId);
            if (checkbox.checked) {
                if (exportSelectedIds.size >= EXPORT_MAX_SCRAPS) {
                    checkbox.checked = false;
                    showGlobalNotification(`<p>Maksymalnie ${EXPORT_MAX_SCRAPS} analiz.</p>`);
                    return;
                }
                exportSelectedIds.add(scrapId);
                row.classList.add('selected');
            } else {
                exportSelectedIds.delete(scrapId);
                row.classList.remove('selected');
            }
            updateExportSelectionUI();
        }

        function updateExportSelectionUI() {
            const count = exportSelectedIds.size;
            document.getElementById('exportSelectionCounter').textContent = `Wybrano: ${count} / ${EXPORT_MAX_SCRAPS}`;
            document.getElementById('exportStartCount').textContent = count;
            document.getElementById('exportStartBtn').disabled = count === 0;

            document.querySelectorAll('.export-scrap-row').forEach(row => {
                const scrapId = parseInt(row.dataset.scrapId);
                const atLimit = count >= EXPORT_MAX_SCRAPS && !exportSelectedIds.has(scrapId);
                row.classList.toggle('disabled-row', atLimit);
                row.querySelector('input[type="checkbox"]').disabled = atLimit;
            });
        }

        document.getElementById('exportSelectLastBtn').addEventListener('click', function () {
            exportSelectedIds.clear();
            if (exportAvailableScraps.length > 0) exportSelectedIds.add(exportAvailableScraps[0].id);
            refreshScrapCheckboxes();
            updateExportSelectionUI();
        });

        document.getElementById('exportSelectLast7Btn').addEventListener('click', function () {
            exportSelectedIds.clear();
            const limit = Math.min(7, exportAvailableScraps.length);
            for (let i = 0; i < limit; i++) exportSelectedIds.add(exportAvailableScraps[i].id);
            refreshScrapCheckboxes();
            updateExportSelectionUI();
        });

        document.getElementById('exportDeselectAllBtn').addEventListener('click', function () {
            exportSelectedIds.clear();
            refreshScrapCheckboxes();
            updateExportSelectionUI();
        });

        function refreshScrapCheckboxes() {
            document.querySelectorAll('.export-scrap-row').forEach(row => {
                const scrapId = parseInt(row.dataset.scrapId);
                const isSelected = exportSelectedIds.has(scrapId);
                row.querySelector('input[type="checkbox"]').checked = isSelected;
                row.classList.toggle('selected', isSelected);
            });
        }

        function showExportStep(step) {
            document.getElementById('exportStep_select').style.display = step === 'select' ? '' : 'none';
            document.getElementById('exportStep_progress').style.display = step === 'progress' ? '' : 'none';

            if (step === 'progress') {
                document.getElementById('exportErrorBox').style.display = 'none';
                document.getElementById('exportProgressBar').style.width = '0%';
                document.getElementById('exportProgressPercent').textContent = '0%';
                document.getElementById('exportProgressStatus').textContent = 'Przygotowywanie...';
                document.getElementById('exportProgressDetail').innerHTML = '&nbsp;';
                document.getElementById('exportStatScraps').textContent = '0 / 0';
                document.getElementById('exportStatPrices').textContent = '0';
            }
        }

        document.getElementById('exportStartBtn').addEventListener('click', async function () {
            if (exportSelectedIds.size === 0) return;

            const scrapIds = Array.from(exportSelectedIds);
            const exportType = document.querySelector('input[name="exportType"]:checked')?.value || 'prices';

            showExportStep('progress');

            try {
                const connectionId = await ensureSignalRConnection();

                // ZMIANA: Strzał do nowego API
                const response = await fetch(`/api/MassExport/ExportMultiScraps?storeId=${storeId}`, {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({
                        scrapIds: scrapIds,
                        connectionId: connectionId,
                        exportType: exportType
                    })
                });

                if (!response.ok) {
                    const errorText = await response.text();
                    throw new Error(errorText || `Błąd serwera: ${response.status}`);
                }

                const disposition = response.headers.get('Content-Disposition');
                let filename = 'eksport.xlsx';
                if (disposition) {
                    const match = disposition.match(/filename\*?=(?:UTF-8''|"?)([^";]+)/i);
                    if (match) filename = decodeURIComponent(match[1].replace(/"/g, ''));
                }

                const blob = await response.blob();

                updateExportProgressUI({
                    step: 'complete',
                    percentComplete: 100,
                    grandTotalPrices: parseInt(document.getElementById('exportStatPrices').textContent.replace(/\s/g, '')) || 0
                });

                const downloadUrl = URL.createObjectURL(blob);
                const a = document.createElement('a');
                a.href = downloadUrl;
                a.download = filename;
                document.body.appendChild(a);
                a.click();
                document.body.removeChild(a);
                URL.revokeObjectURL(downloadUrl);

                setTimeout(() => { $('#exportScrapSelectorModal').modal('hide'); }, 1200);

            } catch (error) {
                console.error('Błąd eksportu:', error);
                document.getElementById('exportErrorBox').style.display = '';
                document.getElementById('exportErrorText').textContent = error.message || 'Wystąpił błąd.';
            }
        });

        async function ensureSignalRConnection() {
            if (exportHubConnection?.state === signalR.HubConnectionState.Connected) {
                return exportHubConnection.connectionId;
            }

            if (exportHubConnection) {
                try { await exportHubConnection.stop(); } catch { }
            }

            exportHubConnection = new signalR.HubConnectionBuilder()
                .withUrl(EXPORT_HUB_URL)
                .withAutomaticReconnect()
                .build();

            exportHubConnection.on('ExportProgress', function (data) {
                updateExportProgressUI(data);
            });

            await exportHubConnection.start();
            return exportHubConnection.connectionId;
        }

        function updateExportProgressUI(data) {
            const bar = document.getElementById('exportProgressBar');
            const percent = document.getElementById('exportProgressPercent');
            const status = document.getElementById('exportProgressStatus');
            const detail = document.getElementById('exportProgressDetail');
            const statScraps = document.getElementById('exportStatScraps');
            const statPrices = document.getElementById('exportStatPrices');
            const icon = document.getElementById('exportProgressIcon');

            const pct = data.percentComplete || 0;
            bar.style.width = pct + '%';
            percent.textContent = pct + '%';
            statScraps.textContent = `${data.currentIndex || 0} / ${data.totalScraps || 0}`;
            if (data.grandTotalPrices != null) statPrices.textContent = data.grandTotalPrices.toLocaleString('pl-PL');

            switch (data.step) {
                case 'processing':
                    icon.className = 'fa-solid fa-gears';
                    icon.style.color = '#198754';
                    status.textContent = `Przetwarzanie analizy ${data.currentIndex} z ${data.totalScraps}`;
                    detail.textContent = data.scrapDate
                        ? `Data: ${data.scrapDate}` + (data.priceCount ? ` — ${data.priceCount.toLocaleString('pl-PL')} cen` : '') : '';
                    break;
                case 'writing':
                    status.textContent = `Zapisywanie arkusza ${data.currentIndex} z ${data.totalScraps}`;
                    detail.textContent = data.scrapDate
                        ? `Data: ${data.scrapDate} — ${(data.priceCount || 0).toLocaleString('pl-PL')} cen` : '';
                    break;
                case 'finalizing':
                    icon.className = 'fa-solid fa-check-circle';
                    status.textContent = 'Finalizowanie pliku...';
                    detail.textContent = `Łącznie: ${(data.grandTotalPrices || 0).toLocaleString('pl-PL')} cen`;
                    break;
                case 'complete':
                    icon.className = 'fa-solid fa-circle-check';
                    status.textContent = 'Pobieranie pliku...';
                    detail.textContent = `Łącznie: ${(data.grandTotalPrices || 0).toLocaleString('pl-PL')} cen`;
                    break;
            }
        }

        $('#exportScrapSelectorModal').on('hidden.bs.modal', function () {
            if (exportHubConnection) {
                exportHubConnection.stop().catch(() => { });
                exportHubConnection = null;
            }
        });
    }
});