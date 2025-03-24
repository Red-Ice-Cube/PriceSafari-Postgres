// Funkcja globalna, żeby móc ją wywołać z zewnątrz (np. z buttona "Otwórz modal" w widoku).
window.openCompetitorsModal = function () {
    // Otwieramy modal
    $('#competitorModal').modal('show');
};

// Gdy HTML się załaduje, podpinamy logikę do przycisku:
document.addEventListener('DOMContentLoaded', function () {

    const btn = document.getElementById('computeCompetitorsBtn');
    if (!btn) {
        console.error("Przycisk computeCompetitorsBtn nie istnieje!");
        return;
    }

    btn.addEventListener('click', function () {
        // Pobieramy z <select> wybrane źródło
        const ourSource = document.getElementById("ourSourceSelect").value; // "All"/"Google"/"Ceneo"

        // Zakładam, że storeId masz globalnie, np. var storeId = @ViewBag.StoreId;
        const url = `/PriceHistory/GetCompetitorStoresData?storeId=${storeId}&ourSource=${ourSource}`;

        fetch(url)
            .then(response => response.json())
            .then(result => {
                if (!result.data) {
                    console.error("Brak danych konkurentów");
                    return;
                }

                // Dwie osobne listy:
                const googleData = result.data.filter(x => x.dataSource === "Google");
                const ceneoData = result.data.filter(x => x.dataSource === "Ceneo");

                // Czyścimy obie tabele
                const googleTbody = document.getElementById("googleCompetitorsTableBody");
                const ceneoTbody = document.getElementById("ceneoCompetitorsTableBody");
                googleTbody.innerHTML = "";
                ceneoTbody.innerHTML = "";

                // Funkcja do tworzenia wierszy
                function createRow(item) {
                    const tr = document.createElement("tr");

                    // Sklep
                    const storeNameTd = document.createElement("td");
                    storeNameTd.textContent = item.storeName;
                    tr.appendChild(storeNameTd);

                    // Źródło
                    const sourceTd = document.createElement("td");
                    sourceTd.textContent = item.dataSource;
                    tr.appendChild(sourceTd);

                    // Wspólne produkty
                    const commonCountTd = document.createElement("td");
                    commonCountTd.textContent = item.commonProductsCount;
                    tr.appendChild(commonCountTd);

                    // Akcja: "Załaduj te ceny"
                    const actionTd = document.createElement("td");
                    const chooseBtn = document.createElement("button");
                    chooseBtn.textContent = "Załaduj te ceny";
                    chooseBtn.addEventListener("click", function () {
                        // competitorStore -> globalna zmienna
                        window.competitorStore = item.storeName;
                        // Możesz też ustawić window.source = item.dataSource, żeby w getPrices
                        // odfiltrować Google/Ceneo

                        if (typeof loadPrices === "function") {
                            loadPrices();
                        } else {
                            console.error("Brak funkcji loadPrices!");
                        }

                        // Zamykamy modal (Bootstrap)
                        $('#competitorModal').modal('hide');
                    });
                    actionTd.appendChild(chooseBtn);
                    tr.appendChild(actionTd);

                    return tr;
                }

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
            })
            .catch(err => console.error("Błąd podczas pobierania konkurentów:", err));
    });
});
