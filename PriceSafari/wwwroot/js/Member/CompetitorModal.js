/*****************************************************
 * CompetitorModal.js – obsługa modala z presetami
 *****************************************************/

// 1) Otwarcie modala – wywołane np. onclick="openCompetitorsModal()"
window.openCompetitorsModal = function () {
    // Załaduj listę istniejących presetów i pokaż modal
    loadPresetList();
    $('#competitorModal').modal('show');
};

// 2) Gdy DOM się załaduje, podpinamy eventy do przycisków:
document.addEventListener('DOMContentLoaded', function () {

    // a) Oblicz konkurentów (computeCompetitorsBtn)
    const btnCompute = document.getElementById('computeCompetitorsBtn');
    if (btnCompute) {
        btnCompute.addEventListener('click', function () {
            computeCompetitors();
        });
    }

    // b) Dodaj nowy preset
    const btnAddNewPreset = document.getElementById("addNewPresetBtn");
    if (btnAddNewPreset) {
        btnAddNewPreset.addEventListener("click", function () {
            document.getElementById("newPresetSection").style.display = "block";
            document.getElementById("presetNameInput").value = "";
            document.getElementById("nowInUseCheckbox").checked = false;
        });
    }

    // c) Załaduj wybrany preset
    const btnLoadPreset = document.getElementById("loadPresetBtn");
    if (btnLoadPreset) {
        btnLoadPreset.addEventListener("click", function () {
            loadSelectedPreset();
        });
    }

    // d) Zapisanie preset-u
    const btnSavePreset = document.getElementById("savePresetBtn");
    if (btnSavePreset) {
        btnSavePreset.addEventListener("click", function () {
            savePreset();
        });
    }
});

/*****************************************************
 * Funkcja pobierająca listę istniejących presetów
 *****************************************************/
function loadPresetList() {
    // Załóżmy, że masz endpoint: GET /PriceHistory/GetPresets?storeId=...
    const url = `/PriceHistory/GetPresets?storeId=${storeId}`;

    fetch(url)
        .then(r => r.json())
        .then(list => {
            const presetSelect = document.getElementById("presetSelect");
            if (!presetSelect) return;
            presetSelect.innerHTML = "";

            const defaultOpt = document.createElement("option");
            defaultOpt.value = "";
            defaultOpt.textContent = "-- wybierz preset --";
            presetSelect.appendChild(defaultOpt);

            list.forEach(p => {
                const opt = document.createElement("option");
                opt.value = p.presetId;  // np. p.presetId
                opt.textContent = p.presetName + (p.nowInUse ? " [aktywny]" : "");
                presetSelect.appendChild(opt);
            });
        })
        .catch(err => console.error("Błąd ładowania listy presetów:", err));
}

/*****************************************************
 * Funkcja wczytująca detale wybranego presetu
 *****************************************************/
function loadSelectedPreset() {
    const presetSelect = document.getElementById("presetSelect");
    if (!presetSelect) return;

    const presetId = presetSelect.value;
    if (!presetId) {
        alert("Nie wybrano presetu.");
        return;
    }

    // Endpoint np. /PriceHistory/GetPresetDetails?presetId=...
    const url = `/PriceHistory/GetPresetDetails?presetId=${presetId}`;

    fetch(url)
        .then(r => r.json())
        .then(preset => {
            if (!preset) {
                console.error("Brak preset-u w odpowiedzi");
                return;
            }

            // Zapisz globalnie – by móc z niego skorzystać:
            window.currentPreset = preset;

            // Ustaw polakom
            document.getElementById("newPresetSection").style.display = "block";
            document.getElementById("presetNameInput").value = preset.presetName || "";
            document.getElementById("nowInUseCheckbox").checked = preset.nowInUse;
            document.getElementById("useUnmarkedStoresCheckbox").checked = preset.useUnmarkedStores;

            // Ustaw <select> ourSourceSelect w zależności od (preset.sourceGoogle, preset.sourceCeneo)
            let ourSource = "All";
            if (preset.sourceGoogle && !preset.sourceCeneo) ourSource = "Google";
            if (!preset.sourceGoogle && preset.sourceCeneo) ourSource = "Ceneo";
            document.getElementById("ourSourceSelect").value = ourSource;

            // Po wczytaniu parametru – obliczamy konkurentów:
            computeCompetitors();
        })
        .catch(err => console.error("Błąd ładowania detali preset-u:", err));
}

/*****************************************************
 * Funkcja ładująca listę konkurentów z API
 *****************************************************/
function computeCompetitors() {
    const ourSource = document.getElementById("ourSourceSelect").value; // "All"/"Google"/"Ceneo"
    const url = `/PriceHistory/GetCompetitorStoresData?storeId=${storeId}&ourSource=${ourSource}`;

    fetch(url)
        .then(response => response.json())
        .then(result => {
            if (!result.data) {
                console.error("Brak danych konkurentów");
                return;
            }

            // Podziel na Google/Ceneo
            const googleData = result.data.filter(x => x.dataSource === "Google");
            const ceneoData = result.data.filter(x => x.dataSource === "Ceneo");

            // Czyścimy obie tabele
            const googleTbody = document.getElementById("googleCompetitorsTableBody");
            const ceneoTbody = document.getElementById("ceneoCompetitorsTableBody");
            googleTbody.innerHTML = "";
            ceneoTbody.innerHTML = "";

            // Wypełniamy tabelę google
            googleData.forEach(item => {
                const row = createRow(item);
                googleTbody.appendChild(row);
            });

            // Wypełniamy tabelę ceneo
            ceneoData.forEach(item => {
                const row = createRow(item);
                ceneoTbody.appendChild(row);
            });

            // Jeśli załadowaliśmy wcześniej preset – zaznaczyć checkboxy
            if (window.currentPreset && window.currentPreset.competitorItems) {
                markPresetCompetitors();
            }

        })
        .catch(err => console.error("Błąd podczas pobierania konkurentów:", err));
}

/*****************************************************
 * Tworzenie pojedynczego wiersza w tabeli
 *****************************************************/
function createRow(item) {
    const tr = document.createElement("tr");

    // 1) Sklep
    const storeNameTd = document.createElement("td");
    storeNameTd.textContent = item.storeName;
    tr.appendChild(storeNameTd);

    // 2) Źródło
    const sourceTd = document.createElement("td");
    sourceTd.textContent = item.dataSource;
    tr.appendChild(sourceTd);

    // 3) Wspólne produkty
    const commonCountTd = document.createElement("td");
    commonCountTd.textContent = item.commonProductsCount;
    tr.appendChild(commonCountTd);

    // 4) Akcja: "Załaduj te ceny"
    const actionTd = document.createElement("td");
    const chooseBtn = document.createElement("button");
    chooseBtn.textContent = "Załaduj te ceny";
    chooseBtn.addEventListener("click", function () {
        window.competitorStore = item.storeName;
        // ewentualnie window.source = item.dataSource;

        if (typeof loadPrices === "function") {
            loadPrices();
        } else {
            console.error("Brak funkcji loadPrices!");
        }

        $('#competitorModal').modal('hide');
    });
    actionTd.appendChild(chooseBtn);
    tr.appendChild(actionTd);

    // 5) Kolumna: "W preset?"
    const presetTd = document.createElement("td");
    const presetCheckbox = document.createElement("input");
    presetCheckbox.type = "checkbox";
    presetCheckbox.checked = false; // domyślnie false
    presetTd.appendChild(presetCheckbox);
    tr.appendChild(presetTd);

    // Zapisz dane do tr (ułatwia identyfikację)
    tr.dataset.storeName = item.storeName;
    tr.dataset.dataSource = item.dataSource; // "Google"/"Ceneo"

    return tr;
}

/*****************************************************
 * Zaznacza checkboxy wierszy na podstawie currentPreset
 *****************************************************/
function markPresetCompetitors() {
    const allRows = document.querySelectorAll("#googleCompetitorsTableBody tr, #ceneoCompetitorsTableBody tr");
    if (!allRows) return;

    window.currentPreset.competitorItems.forEach(ci => {
        // ci.storeName, ci.isGoogle, ci.useCompetitor
    });

    allRows.forEach(tr => {
        const storeName = tr.dataset.storeName;
        const dataSource = tr.dataset.dataSource; // "Google"/"Ceneo"
        const checkbox = tr.querySelector("input[type='checkbox']");

        // Szukamy, czy jest w competitorItems
        const match = window.currentPreset.competitorItems.find(ci => {
            if (ci.storeName !== storeName) return false;
            if (ci.isGoogle && dataSource !== "Google") return false;
            if (!ci.isGoogle && dataSource !== "Ceneo") return false;
            return true;
        });
        // Jeśli znaleźliśmy i match.useCompetitor == true, to zaznacz checkbox
        if (match && match.useCompetitor && checkbox) {
            checkbox.checked = true;
        }
    });
}

/*****************************************************
 * Zapisanie preset-u (SaveCompetitorPreset)
 *****************************************************/
function savePreset() {
    const presetName = document.getElementById("presetNameInput").value.trim();
    const nowInUse = document.getElementById("nowInUseCheckbox").checked;
    const useUnmarkedStores = document.getElementById("useUnmarkedStoresCheckbox").checked;

    // google/ceneo?
    let sourceGoogle = false;
    let sourceCeneo = false;
    const ourSource = document.getElementById("ourSourceSelect").value;
    if (ourSource === "All") {
        sourceGoogle = true;
        sourceCeneo = true;
    } else if (ourSource === "Google") {
        sourceGoogle = true;
        sourceCeneo = false;
    } else if (ourSource === "Ceneo") {
        sourceGoogle = false;
        sourceCeneo = true;
    }

    // Zbieramy wiersze
    const googleRows = document.querySelectorAll("#googleCompetitorsTableBody tr");
    const ceneoRows = document.querySelectorAll("#ceneoCompetitorsTableBody tr");
    const allRows = [...googleRows, ...ceneoRows];

    const competitorItems = allRows.map(tr => {
        const storeName = tr.dataset.storeName;
        const dataSource = tr.dataset.dataSource; // "Google" / "Ceneo"
        const checkbox = tr.querySelector("input[type='checkbox']");
        const useCompetitor = checkbox ? checkbox.checked : false;
        return {
            storeName,
            isGoogle: (dataSource === "Google"),
            useCompetitor
        };
    });

    const payload = {
        storeId: storeId,
        presetName,
        nowInUse,
        sourceGoogle,
        sourceCeneo,
        useUnmarkedStores,
        competitorItems
    };

    fetch("/PriceHistory/SaveCompetitorPreset", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(payload)
    })
        .then(r => r.json())
        .then(data => {
            if (data.success) {
                alert(`Zapisano preset ID=${data.presetId}`);
                // odśwież listę presetów
                loadPresetList();
            } else {
                alert("Błąd przy zapisie preset-u");
            }
        })
        .catch(err => console.error("Błąd zapisu preset-u:", err));
}
