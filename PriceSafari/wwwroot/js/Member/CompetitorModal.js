/*****************************************************
 * CompetitorModal.js – obsługa modala z presetami,
 * z jedną metodą SaveOrUpdatePreset na backendzie.
 *
 * ZMIANY:
 *  - nazwa presetu zapisywana TYLKO po kliknięciu w "Zmień nazwę"
 *  - tabela Ceneo/Google wyszarzona i bez przycisków, jeśli dane źródło wyłączone.
 *****************************************************/

// Globalnie trzymamy aktualny preset (lub null w widoku bazowym)
window.currentPreset = null;

/**
 * Otwiera modal i ładuje listę presetów.
 */
window.openCompetitorsModal = async function () {
    const presets = await fetchPresets();

    const presetSelect = document.getElementById("presetSelect");
    if (!presetSelect) return;

    // Czyścimy <select> i dodajemy "Widok bazowy"
    presetSelect.innerHTML = "";
    const baseOpt = document.createElement("option");
    baseOpt.value = "BASE";
    baseOpt.textContent = "(Widok bazowy)";
    presetSelect.appendChild(baseOpt);

    // Dodajemy presety
    presets.forEach(p => {
        const opt = document.createElement("option");
        opt.value = p.presetId;
        opt.textContent = p.presetName + (p.nowInUse ? " [aktywny]" : "");
        presetSelect.appendChild(opt);
    });

    // Jeśli istnieje nowInUse, ustaw go jako wybrany
    const active = presets.find(x => x.nowInUse === true);
    if (active) {
        presetSelect.value = active.presetId;
        await loadSelectedPreset(active.presetId);
    } else {
        presetSelect.value = "BASE";
        loadBaseView();
    }

    $('#competitorModal').modal('show');
};

/**
 * Pobiera listę presetów z bazy.
 */
async function fetchPresets() {
    const url = `/PriceHistory/GetPresets?storeId=${storeId}`;
    try {
        const resp = await fetch(url);
        return await resp.json();
    } catch (err) {
        console.error("fetchPresets error", err);
        return [];
    }
}

/**
 * Widok bazowy - brak presetId => nic nie zapisujemy.
 */
function loadBaseView() {
    window.currentPreset = null;
    document.getElementById("newPresetSection").style.display = "none";

    // Ładujemy ALL
    loadCompetitors("All");
}

/**
 * Zmiana w <select> - wczytujemy wybrany preset
 */
document.addEventListener("DOMContentLoaded", () => {
    const presetSelect = document.getElementById("presetSelect");
    if (presetSelect) {
        presetSelect.addEventListener("change", async function () {
            if (this.value === "BASE") {
                loadBaseView();
            } else {
                await loadSelectedPreset(this.value);
            }
        });
    }
});

/**
 * Wczytanie szczegółów wybranego presetu
 */
async function loadSelectedPreset(presetId) {
    if (!presetId) {
        const presetSelect = document.getElementById("presetSelect");
        if (presetSelect) presetId = presetSelect.value;
    }
    if (!presetId || presetId === "BASE") {
        loadBaseView();
        return;
    }

    try {
        const resp = await fetch(`/PriceHistory/GetPresetDetails?presetId=${presetId}`);
        const preset = await resp.json();

        window.currentPreset = {
            presetId: preset.presetId,
            storeId: storeId,
            presetName: preset.presetName,
            nowInUse: preset.nowInUse,
            sourceGoogle: preset.sourceGoogle,
            sourceCeneo: preset.sourceCeneo,
            useUnmarkedStores: preset.useUnmarkedStores,
            competitors: (preset.competitorItems || []).map(ci => ({
                storeName: ci.storeName,
                isGoogle: ci.isGoogle,
                useCompetitor: ci.useCompetitor
            }))
        };

        document.getElementById("newPresetSection").style.display = "block";
        document.getElementById("presetNameInput").value = preset.presetName || "";
        document.getElementById("nowInUseCheckbox").checked = preset.nowInUse;
        document.getElementById("useUnmarkedStoresCheckbox").checked = preset.useUnmarkedStores;

        // Ustaw checkboksy
        const googleChk = document.getElementById("googleCheckbox");
        const ceneoChk = document.getElementById("ceneoCheckbox");
        if (googleChk) googleChk.checked = !!preset.sourceGoogle;
        if (ceneoChk) ceneoChk.checked = !!preset.sourceCeneo;

        // Na bazie googleChk/ceneoChk wczytujemy sklepy
        const sourceVal = determineSourceVal(!!preset.sourceGoogle, !!preset.sourceCeneo);
        loadCompetitors(sourceVal);
    } catch (err) {
        console.error("loadSelectedPreset error", err);
    }
}

/**
 * Funkcja ustalająca "ourSource" dla endpointu w zależności od boole'ów:
 */
function determineSourceVal(isGoogle, isCeneo) {
    if (isGoogle && isCeneo) return "All";
    if (isGoogle) return "Google";
    if (isCeneo) return "Ceneo";
    // W skrajnym wypadku żadnego => "All" (lub można nic nie wczytać).
    return "All";
}

/**
 * Ładuje sklepy z backendu i wypełnia tabele.
 */
function loadCompetitors(ourSource) {
    const url = `/PriceHistory/GetCompetitorStoresData?storeId=${storeId}&ourSource=${ourSource}`;
    fetch(url)
        .then(r => r.json())
        .then(data => {
            if (!data.data) {
                console.warn("Brak data");
                return;
            }
            const googleData = data.data.filter(x => x.dataSource === "Google");
            const ceneoData = data.data.filter(x => x.dataSource === "Ceneo");

            const gBody = document.getElementById("googleCompetitorsTableBody");
            const cBody = document.getElementById("ceneoCompetitorsTableBody");
            if (gBody) gBody.innerHTML = "";
            if (cBody) cBody.innerHTML = "";

            googleData.forEach(i => gBody.appendChild(createRow(i)));
            ceneoData.forEach(i => cBody.appendChild(createRow(i)));

            markPresetCompetitors();
        })
        .catch(err => console.error(err));
}

/**
 * Tworzy wiersz w tabeli. Jeśli źródło jest nieaktywne (np. Ceneo= false),
 * to wyszarzamy i nie dajemy przycisków Dodaj/Usuń.
 */
function createRow(item) {
    const tr = document.createElement("tr");

    // Nazwa sklepu
    const tdStore = document.createElement("td");
    tdStore.textContent = item.storeName;
    tr.appendChild(tdStore);

    // Źródło
    const tdSource = document.createElement("td");
    tdSource.textContent = item.dataSource;
    tr.appendChild(tdSource);

    // Wspólne produkty
    const tdCommon = document.createElement("td");
    tdCommon.textContent = item.commonProductsCount;
    tr.appendChild(tdCommon);

    // Czy dane źródło jest aktywne w presencie?
    let sourceActive = true;
    if (item.dataSource === "Google" && !window.currentPreset.sourceGoogle) {
        sourceActive = false;
    }
    if (item.dataSource === "Ceneo" && !window.currentPreset.sourceCeneo) {
        sourceActive = false;
    }

    const tdAction = document.createElement("td");
    if (!sourceActive) {
        // Wyszarzamy cały wiersz i brak akcji
        tr.style.backgroundColor = "#cccccc"; // jasnoszare tło
        tdAction.textContent = "Niedostępne";
    } else {
        // Normalnie dodajemy przyciski
        const addBtn = document.createElement("button");
        addBtn.textContent = "Dodaj";
        addBtn.style.marginRight = "5px";
        addBtn.addEventListener("click", () => {
            toggleCompetitorUsage(item.storeName, (item.dataSource === "Google"), true);
        });
        tdAction.appendChild(addBtn);

        const remBtn = document.createElement("button");
        remBtn.textContent = "Usuń";
        remBtn.addEventListener("click", () => {
            toggleCompetitorUsage(item.storeName, (item.dataSource === "Google"), false);
        });
        tdAction.appendChild(remBtn);
    }
    tr.appendChild(tdAction);

    tr.dataset.storeName = item.storeName;
    tr.dataset.dataSource = item.dataSource;

    return tr;
}

/**
 * Dodaj/usuń sklep w currentPreset.competitors + zapisz
 */
function toggleCompetitorUsage(storeName, isGoogle, useCompetitor) {
    // Widok bazowy
    if (!window.currentPreset || !window.currentPreset.presetId) {
        alert("Widok bazowy – nie zapisujemy do bazy.");
        return;
    }
    let comp = window.currentPreset.competitors
        .find(c => c.storeName.toLowerCase() === storeName.toLowerCase() && c.isGoogle === isGoogle);

    if (!comp) {
        comp = { storeName, isGoogle, useCompetitor };
        window.currentPreset.competitors.push(comp);
    } else {
        comp.useCompetitor = useCompetitor;
    }

    saveOrUpdatePreset();
    refreshRowColor(storeName, isGoogle);
}

/**
 * Po wstawieniu danych, pokoloruj wiersze, by odróżnić
 * te z competitorItems (useCompetitor) i te nieoznaczone.
 */
function markPresetCompetitors() {
    document.querySelectorAll("#googleCompetitorsTableBody tr").forEach(tr => {
        refreshRowColor(tr.dataset.storeName, true);
    });
    document.querySelectorAll("#ceneoCompetitorsTableBody tr").forEach(tr => {
        refreshRowColor(tr.dataset.storeName, false);
    });
}

/**
 * Kolor wiersza (zielony/czerwony) zależnie od competitorItems + useUnmarkedStores
 */
function refreshRowColor(storeName, isGoogle) {
    const ds = isGoogle ? "Google" : "Ceneo";
    const row = document.querySelector(`tr[data-store-name="${storeName}"][data-data-source="${ds}"]`);
    if (!row) return;

    // Jeśli i tak jest sourceActive == false => już jest #cccccc
    // (wyszarzone w createRow). Nie nadpisujemy mu koloru.
    if (!window.currentPreset) return;

    // Sprawdzamy, czy w createRow ustawiliśmy "Niedostępne"
    let sourceActive = true;
    if (ds === "Google" && !window.currentPreset.sourceGoogle) sourceActive = false;
    if (ds === "Ceneo" && !window.currentPreset.sourceCeneo) sourceActive = false;

    if (!sourceActive) {
        // nic nie zmieniamy, jest wyszarzone
        return;
    }

    // Inaczej normalne kolorowanie:
    const item = window.currentPreset.competitors.find(ci =>
        ci.storeName.toLowerCase() === storeName.toLowerCase() && ci.isGoogle === isGoogle
    );
    const useUnmarked = window.currentPreset.useUnmarkedStores;

    if (item) {
        row.style.backgroundColor = item.useCompetitor ? "green" : "darkred";
    } else {
        row.style.backgroundColor = useUnmarked ? "lightgreen" : "lightcoral";
    }
}

/**
 * Zmiana checkboksa useUnmarkedStores => natychmiastowy zapis
 */
document.addEventListener("DOMContentLoaded", () => {
    const chk = document.getElementById("useUnmarkedStoresCheckbox");
    if (!chk) return;
    chk.addEventListener("change", function () {
        if (!window.currentPreset || !window.currentPreset.presetId) {
            if (window.currentPreset) {
                window.currentPreset.useUnmarkedStores = this.checked;
                markPresetCompetitors();
            }
            return;
        }
        window.currentPreset.useUnmarkedStores = this.checked;
        saveOrUpdatePreset();
        markPresetCompetitors();
    });
});

/**
 * Obsługa checkboksów Google/Ceneo => dynamiczna zmiana sourceGoogle/sourceCeneo
 * i zapisywanie (o ile preset != bazowy).
 */
document.addEventListener("DOMContentLoaded", () => {
    const googleChk = document.getElementById("googleCheckbox");
    const ceneoChk = document.getElementById("ceneoCheckbox");
    if (!googleChk || !ceneoChk) return;

    function onSourceChange() {
        const isGoogle = googleChk.checked;
        const isCeneo = ceneoChk.checked;

        if (!window.currentPreset || !window.currentPreset.presetId) {
            // widok bazowy
            const val = determineSourceVal(isGoogle, isCeneo);
            loadCompetitors(val);
            return;
        }

        // Normalnie => zapisz
        window.currentPreset.sourceGoogle = isGoogle;
        window.currentPreset.sourceCeneo = isCeneo;

        saveOrUpdatePreset();
        const sourceVal = determineSourceVal(isGoogle, isCeneo);
        loadCompetitors(sourceVal);
    }

    googleChk.addEventListener("change", onSourceChange);
    ceneoChk.addEventListener("change", onSourceChange);
});

/**
 * Dodanie nowego presetu
 */
document.addEventListener("DOMContentLoaded", () => {
    const btnNew = document.getElementById("addNewPresetBtn");
    if (!btnNew) return;

    btnNew.addEventListener("click", async function () {
        const presetName = prompt("Podaj nazwę nowego presetu:", "");
        if (!presetName) return;

        const newPreset = {
            presetId: 0,
            storeId: storeId,
            presetName,
            nowInUse: false,
            sourceGoogle: true,
            sourceCeneo: true,
            useUnmarkedStores: true,
            competitors: []
        };

        try {
            const resp = await fetch("/PriceHistory/SaveOrUpdatePreset", {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify(newPreset)
            });
            const data = await resp.json();
            if (data.success) {
                alert(`Utworzono nowy preset ID=${data.presetId}`);
                const newPresets = await fetchPresets();
                const presetSelect = document.getElementById("presetSelect");
                if (!presetSelect) return;

                presetSelect.innerHTML = "";
                const baseOpt = document.createElement("option");
                baseOpt.value = "BASE";
                baseOpt.textContent = "(Widok bazowy)";
                presetSelect.appendChild(baseOpt);

                newPresets.forEach(p => {
                    const opt = document.createElement("option");
                    opt.value = p.presetId;
                    opt.textContent = p.presetName + (p.nowInUse ? " [aktywny]" : "");
                    presetSelect.appendChild(opt);
                });
                presetSelect.value = data.presetId;
                await loadSelectedPreset(data.presetId);

            } else {
                alert("Błąd tworzenia nowego presetu");
            }
        } catch (err) {
            console.error("addNewPreset error:", err);
        }
    });
});

/**
 * Zmiana "nowInUse" => może być zapisywana automatycznie
 * lub też w przycisku "Zmień nazwę". Tu wersja auto:
 */
document.addEventListener("DOMContentLoaded", () => {
    const nowInUseCheckbox = document.getElementById("nowInUseCheckbox");
    if (nowInUseCheckbox) {
        nowInUseCheckbox.addEventListener("change", function () {
            if (!window.currentPreset || !window.currentPreset.presetId) return;
            window.currentPreset.nowInUse = this.checked;
            // Zapis + odśwież listę
            saveOrUpdatePreset().then(() => refreshPresetDropdown());
        });
    }

    // Usunięto onBlur w nameInput => brak automatycznego zapisu nazwy
    // => TYLKO przycisk "Zmień nazwę"
    const nameInput = document.getElementById("presetNameInput");
    const editBtn = document.getElementById("editPresetBtn");
    if (editBtn) {
        editBtn.addEventListener("click", async function () {
            if (!window.currentPreset || !window.currentPreset.presetId) {
                alert("Widok bazowy lub brak wybranego presetu");
                return;
            }
            // TYLKO teraz aktualizujemy name i zapisujemy
            window.currentPreset.presetName = nameInput.value.trim();

            // Ewentualnie także nowInUse - ale tu jest już obsługiwane automatycznie
            // więc można pominąć w tym miejscu. 
            // window.currentPreset.nowInUse = nowInUseCheckbox.checked;

            await saveOrUpdatePreset();
            await refreshPresetDropdown();

            alert("Zmieniono nazwę presetu!");
        });
    }
});

/**
 * Zapis w bazie (jeden endpoint).
 */
async function saveOrUpdatePreset() {
    if (!window.currentPreset) return;
    if (!window.currentPreset.presetId) return; // bazowy

    try {
        const resp = await fetch("/PriceHistory/SaveOrUpdatePreset", {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify(window.currentPreset)
        });
        const data = await resp.json();
        if (!data.success) {
            alert("Błąd zapisu presetów: " + (data.message || ""));
            return;
        }
        // w razie nowego presetu - ID
        window.currentPreset.presetId = data.presetId;
    } catch (err) {
        console.error("saveOrUpdatePreset error", err);
        alert("Błąd zapisu (SaveOrUpdatePreset). Sprawdź konsolę.");
    }
}

/**
 * Odświeża listę presetów w <select>.
 */
async function refreshPresetDropdown() {
    const newPresets = await fetchPresets();
    const presetSelect = document.getElementById("presetSelect");
    if (!presetSelect) return;

    presetSelect.innerHTML = "";
    const baseOpt = document.createElement("option");
    baseOpt.value = "BASE";
    baseOpt.textContent = "(Widok bazowy)";
    presetSelect.appendChild(baseOpt);

    newPresets.forEach(p => {
        const opt = document.createElement("option");
        opt.value = p.presetId;
        opt.textContent = p.presetName + (p.nowInUse ? " [aktywny]" : "");
        presetSelect.appendChild(opt);
    });

    if (window.currentPreset && window.currentPreset.presetId) {
        presetSelect.value = window.currentPreset.presetId;
    } else {
        presetSelect.value = "BASE";
    }
}
