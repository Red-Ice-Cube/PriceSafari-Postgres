// Pobranie danych inicjalizacyjnych z atrybutów HTML
const ceneoDataEl = document.getElementById("ceneo-wizard-data");
const storeId = parseInt(ceneoDataEl.getAttribute("data-store-id") || "0", 10);
let existingMappingsJson = ceneoDataEl.getAttribute("data-existing-mappings") || "[]";
let existingMappings = [];
try {
    // Parsowanie istniejących mapowań przekazanych z backendu
    existingMappings = JSON.parse(existingMappingsJson);
} catch (e) {
    console.error("Błąd parsowania existingMappingsJson:", e);
    existingMappings = []; // Użyj pustej tablicy w razie błędu
}

// --- ZMIANA: Dodanie pól cenowych Ceneo ---
// Obiekt przechowujący aktualne mapowania XPath dla każdego pola
let mappingForField = {
    ExternalId: null,
    Url: null,
    CeneoEan: null,
    CeneoImage: null,
    CeneoExportedName: null,
    CeneoXMLPrice: null,         // Nowe pole dla ceny z XML Ceneo
    CeneoDeliveryXMLPrice: null // Nowe pole dla ceny z dostawą z XML Ceneo
};
// --- KONIEC ZMIANY ---

// Aplikowanie istniejących mapowań pobranych z bazy danych
existingMappings.forEach(m => {
    // Sprawdzenie, czy pole z bazy danych istnieje w naszym obiekcie `mappingForField`
    if (m.fieldName && mappingForField.hasOwnProperty(m.fieldName)) {
        mappingForField[m.fieldName] = {
            xpath: m.localName,   // Zapisanie XPath
            nodeCount: 0,         // Licznik węzłów zostanie zaktualizowany później
            firstValue: ""        // Pierwsza znaleziona wartość zostanie zaktualizowana później
        };
    }
});

let xmlDoc = null; // Zmienna przechowująca sparsowany dokument XML
// Konstrukcja URL do proxy pobierającego XML dla danego sklepu
let proxyUrl = `/CeneoImportWizardXml/ProxyXml?storeId=${storeId}`;

// Pobranie i sparsowanie pliku XML
fetch(proxyUrl)
    .then(resp => {
        if (!resp.ok) {
            // Rzucenie błędu jeśli odpowiedź serwera nie jest OK (np. 404, 500)
            throw new Error(`Błąd HTTP ${resp.status}: ${resp.statusText}`);
        }
        return resp.text(); // Odczytanie odpowiedzi jako tekst
    })
    .then(xmlStr => {
        let parser = new DOMParser();
        xmlDoc = parser.parseFromString(xmlStr, "application/xml"); // Parsowanie XML
        // Sprawdzenie, czy wystąpił błąd parsowania
        if (xmlDoc.documentElement.nodeName === "parsererror" || xmlDoc.getElementsByTagName("parsererror").length > 0) {
            let errorMsg = "Błąd parsowania XML:\n";
            // Próba uzyskania bardziej szczegółowego błędu
            const parserError = xmlDoc.getElementsByTagName("parsererror")[0];
            if (parserError) {
                errorMsg += parserError.textContent;
            } else {
                errorMsg += xmlDoc.documentElement.textContent;
            }
            document.getElementById("xmlContainer").innerText = errorMsg;
            xmlDoc = null; // Zresetuj xmlDoc w razie błędu parsowania
            return;
        }
        // Jeśli parsowanie się powiodło, zbuduj drzewo XML w UI
        buildXmlTree(xmlDoc.documentElement, "");
        // Zastosuj i podświetl istniejące mapowania na nowo zbudowanym drzewie
        applyExistingMappings();
    })
    .catch(err => {
        // Obsługa błędów sieciowych lub błędów rzuconych z `resp.ok`
        document.getElementById("xmlContainer").innerText =
            "Błąd pobierania lub parsowania XML:\n" + err;
        console.error("Błąd fetch/parse XML:", err);
    });

// Rekursywna funkcja budująca wizualne drzewo XML w kontenerze #xmlContainer
function buildXmlTree(node, parentPath) {
    let container = document.getElementById("xmlContainer");
    // Jeśli to korzeń i nie ma jeszcze listy `ul`, stwórz ją
    if (!parentPath && !container.querySelector("ul")) {
        let ulRoot = document.createElement("ul");
        ulRoot.appendChild(createLi(node, parentPath)); // Stwórz element listy dla korzenia
        container.appendChild(ulRoot);
    } else {
        // Jeśli to nie korzeń, znajdź listę `ul` i dodaj element `li`
        let rootUl = container.querySelector("ul");
        rootUl.appendChild(createLi(node, parentPath));
    }
}

// Funkcja tworząca element `li` reprezentujący węzeł XML
function createLi(node, parentPath) {
    let li = document.createElement("li");
    li.classList.add("xml-node"); // Klasa dla styli CSS

    let nodeName = node.nodeName; // Nazwa węzła (np. 'o', 'name', 'a')
    // Sprawdzenie, czy węzeł ma atrybut 'name' (częste w Ceneo np. <a name="EAN">)
    let nameAttr = node.getAttribute && node.getAttribute("name");
    if (nameAttr) {
        // Dodanie predykatu atrybutu do nazwy dla XPath
        nodeName += `[@name="${nameAttr}"]`;
    }
    // Budowanie pełnej ścieżki XPath do bieżącego węzła
    let currentPath = parentPath ? parentPath + "/" + nodeName : "/" + nodeName;
    li.setAttribute("data-xpath", currentPath); // Zapisanie XPath jako atrybut data

    // Wyświetlenie nazwy węzła
    let b = document.createElement("b");
    b.innerText = node.nodeName + (nameAttr ? ` (name="${nameAttr}")` : "");
    li.appendChild(b);

    // Wyświetlanie atrybutów węzła (jeśli istnieją i nie są atrybutem 'name')
    if (node.attributes && node.attributes.length > 0) {
        let ulAttrs = document.createElement("ul");
        Array.from(node.attributes).forEach(attr => {
            // Pomijamy atrybut 'name', bo jest już częścią nazwy węzła w XPath
            if (attr.name === "name" && nameAttr) return;

            let attrLi = document.createElement("li");
            attrLi.classList.add("xml-node", "xml-attribute"); // Klasy dla styli
            // Ścieżka XPath do atrybutu
            let attrPath = currentPath + `/@${attr.name}`;
            attrLi.setAttribute("data-xpath", attrPath);

            // Wyświetlenie nazwy i wartości atrybutu
            let attrLabel = document.createElement("span");
            // Użycie innerHTML, aby można było dodać style lub inną strukturę
            attrLabel.innerHTML = `<i style="color: gray;">@${attr.name}:</i> ${attr.value}`;
            attrLi.appendChild(attrLabel);
            ulAttrs.appendChild(attrLi);
        });
        if (ulAttrs.children.length > 0) {
            li.appendChild(ulAttrs);
        }
    }

    // Wyświetlanie wartości tekstowej węzła (jeśli nie ma dzieci i tekst nie jest pusty)
    let textVal = node.textContent.trim();
    // Sprawdzamy czy węzeł zawiera *tylko* tekst (node.children.length === 0)
    // oraz czy ten tekst nie pochodzi z konkatenacji tekstów dzieci (porównujemy z node.innerHTML)
    // Uproszczone sprawdzenie: jeśli nie ma dzieci i textVal istnieje.
    if (node.children.length === 0 && textVal) {
        let ulVal = document.createElement("ul");
        let liVal = document.createElement("li");
        liVal.classList.add("xml-node", "xml-value"); // Klasy dla styli
        // Specjalna ścieżka dla wartości tekstowej (można pominąć '#value' w getVal)
        let valPath = currentPath + "/#value";
        liVal.setAttribute("data-xpath", valPath);

        let spanVal = document.createElement("span");
        spanVal.innerText = `: ${textVal}`; // Wyświetlenie wartości
        liVal.appendChild(spanVal);
        ulVal.appendChild(liVal);
        li.appendChild(ulVal);
    }

    // Rekursywne wywołanie dla dzieci węzła
    if (node.children.length > 0) {
        let ul = document.createElement("ul");
        Array.from(node.children).forEach(child => {
            // Przekazanie bieżącej ścieżki jako ścieżki rodzica
            ul.appendChild(createLi(child, currentPath));
        });
        li.appendChild(ul);
    }
    return li;
}

// Globalny listener kliknięć na dokumencie do obsługi wybierania węzłów XML
document.addEventListener("click", e => {
    // Znalezienie najbliższego klikniętego elementu z klasą .xml-node
    let el = e.target.closest(".xml-node");
    if (!el) return; // Jeśli kliknięto poza węzłem XML, zakończ

    // Pobranie aktualnie wybranego pola do mapowania z dropdowna
    let selectedField = document.getElementById("fieldSelector").value;
    // Pobranie XPath klikniętego węzła
    let xPath = el.getAttribute("data-xpath");

    // Usunięcie poprzedniego podświetlenia dla danego pola
    document.querySelectorAll(`.highlight-${selectedField}`)
        .forEach(x => x.classList.remove(`highlight-${selectedField}`));

    // Znalezienie wszystkich węzłów w drzewie UI pasujących do XPath
    let sameNodes = queryNodesByXPath(xPath);
    // Podświetlenie znalezionych węzłów
    sameNodes.forEach(n => n.classList.add(`highlight-${selectedField}`));

    // Aktualizacja informacji o mapowaniu dla wybranego pola
    let count = sameNodes.length;
    let firstVal = "";
    if (count > 0) {
        // Próba pobrania wyświetlanej wartości z pierwszego pasującego węzła
        // Szukamy elementu span wewnątrz węzła (może zawierać wartość lub nazwę atrybutu)
        let sp = sameNodes[0].querySelector("span");
        if (sp) {
            // Proste czyszczenie - usunięcie ewentualnego ': ' na początku
            firstVal = sp.innerText.replace(/^(\s*:\s*)/, "").trim();
            // Jeśli to atrybut, może zawierać HTML - pobierzmy textContent
            if (sameNodes[0].classList.contains('xml-attribute')) {
                firstVal = sp.textContent.replace(/^(\s*@\w+:\s*)/, "").trim();
            }
        }
    }
    // Zapisanie informacji o mapowaniu w obiekcie `mappingForField`
    mappingForField[selectedField] = { xpath: xPath, nodeCount: count, firstValue: firstVal };
    // Odświeżenie tabeli z mapowaniami
    renderMappingTable();
});

// Funkcja pomocnicza do znajdowania węzłów w drzewie UI na podstawie XPath
// Używa atrybutu data-xpath, więc nie jest to prawdziwy ewaluator XPath
function queryNodesByXPath(xPathValue) {
    // Eskapowanie znaków specjalnych dla selektora CSS
    let esc = escapeForSelector(xPathValue);
    return document.querySelectorAll(`.xml-node[data-xpath="${esc}"]`);
}
// Funkcja pomocnicza do eskapowania znaków dla selektora CSS
function escapeForSelector(val) {
    if (!val) return "";
    // Podwójny backslash dla backslasha, backslash dla cudzysłowu
    return val.replace(/\\/g, "\\\\").replace(/"/g, '\\"');
}

// Funkcja stosująca istniejące mapowania (podświetla węzły i aktualizuje tabelę)
function applyExistingMappings() {
    for (let fieldName in mappingForField) {
        let info = mappingForField[fieldName];
        // Sprawdzenie, czy dla danego pola istnieje zapisane mapowanie XPath
        if (!info || !info.xpath) continue;

        // Znalezienie pasujących węzłów w UI
        let sameNodes = queryNodesByXPath(info.xpath);
        let count = sameNodes.length;
        let firstVal = "";
        if (count > 0) {
            // Próba pobrania wartości z pierwszego węzła (podobnie jak w listenerze click)
            let sp = sameNodes[0].querySelector("span");
            if (sp) {
                firstVal = sp.innerText.replace(/^(\s*:\s*)/, "").trim();
                if (sameNodes[0].classList.contains('xml-attribute')) {
                    firstVal = sp.textContent.replace(/^(\s*@\w+:\s*)/, "").trim();
                }
            }
        }
        // Aktualizacja licznika i pierwszej wartości w obiekcie mapowania
        info.nodeCount = count;
        info.firstValue = firstVal;
        // Podświetlenie znalezionych węzłów
        sameNodes.forEach(n => n.classList.add(`highlight-${fieldName}`));
    }
    // Odświeżenie tabeli mapowań
    renderMappingTable();
}

// Funkcja usuwająca wszystkie podświetlenia ze wszystkich węzłów
function clearAllHighlights() {
    Object.keys(mappingForField).forEach(field => {
        document.querySelectorAll(`.highlight-${field}`)
            .forEach(el => el.classList.remove(`highlight-${field}`));
    });
}

// Funkcja renderująca tabelę podsumowującą aktualne mapowania
function renderMappingTable() {
    let tbody = document.getElementById("mappingTable").querySelector("tbody");
    tbody.innerHTML = ""; // Wyczyszczenie tabeli
    // Iteracja przez wszystkie pola możliwe do zmapowania
    for (let fieldName in mappingForField) {
        let info = mappingForField[fieldName]; // Pobranie informacji o mapowaniu dla pola
        let tr = document.createElement("tr"); // Stworzenie wiersza tabeli
        // Jeśli dla pola nie ma mapowania (info jest null lub nie ma xpath)
        if (!info || !info.xpath) {
            tr.innerHTML = `<td>${fieldName}</td><td>-</td><td>0</td><td>-</td>`;
        } else {
            // Jeśli jest mapowanie, wypełnij komórki danymi
            tr.innerHTML = `
                <td>${fieldName}</td>
                <td>${info.xpath || "-"}</td>
                <td>${info.nodeCount || 0}</td>
                <td>${info.firstValue || "-"}</td>
            `;
        }
        tbody.appendChild(tr); // Dodanie wiersza do tabeli
    }
}

// --- DODANA FUNKCJA PARSEPRICE ---
// Funkcja do parsowania wartości tekstowej na liczbę dziesiętną (decimal)
function parsePrice(value) {
    if (value === null || value === undefined || value === '') return null; // Jawne sprawdzenie null/undefined/pusty string
    // Użyj bardziej odpornego regexa:
    // - opcjonalny znak +/- na początku
    // - cyfry, mogą być rozdzielone spacjami lub kropkami (jako tysiące)
    // - opcjonalny separator dziesiętny (przecinek lub kropka)
    // - cyfry po separatorze
    // - ignoruje symbole walut na końcu (np. zł, PLN, EUR)
    let match = String(value).match(/^[+-]?([\d.,\s]+)/); // Pobierz część liczbową z początku
    if (!match) return null; // Jeśli nic nie pasuje

    // Usuń spacje i kropki (tysiące), zamień przecinek na kropkę (dziesiętne)
    let numericString = match[1].replace(/\s/g, '').replace(/\./g, '').replace(',', '.');

    // Sprawdź, czy ostatnia kropka jest separatorem dziesiętnym, jeśli były inne kropki
    const lastDotIndex = numericString.lastIndexOf('.');
    if (lastDotIndex !== -1) {
        const firstDotIndex = numericString.indexOf('.');
        // Jeśli jest więcej niż jedna kropka, a ostatnia nie jest na końcu po 2 cyfrach, coś jest źle
        // Prostsze podejście: zakładamy, że ostatni separator (kropka lub przecinek w oryginale) jest dziesiętny
        // Kod powyżej już zamienił przecinek na kropkę i usunął inne kropki
    }


    let floatVal = parseFloat(numericString); // Sparsuj na liczbę zmiennoprzecinkową
    if (isNaN(floatVal)) return null; // Jeśli wynik nie jest liczbą, zwróć null

    // Zwróć jako string z dwoma miejscami po przecinku - bezpieczniejsze dla decimal na backendzie
    // Backend powinien umieć sparsować string "123.45" na decimal.
    // Można też zwrócić liczbę: return floatVal; ale trzeba uważać na precyzję float.
    return floatVal.toFixed(2);
}
// --- KONIEC DODANEJ FUNKCJI ---


// Listener dla przycisku "Zapisz Mapowania XPath"
document.getElementById("saveMapping").addEventListener("click", () => {
    let finalMappings = [];
    // Zebranie tylko tych pól, które mają zdefiniowane mapowanie XPath
    for (let fieldName in mappingForField) {
        let info = mappingForField[fieldName];
        if (info && info.xpath) {
            // Format danych zgodny z oczekiwaniami endpointu backendowego
            finalMappings.push({ fieldName: fieldName, localName: info.xpath });
        }
    }
    // Wysłanie mapowań do backendu
    fetch(`/CeneoImportWizardXml/SaveCeneoMappings?storeId=${storeId}`, { // Endpoint dla Ceneo
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(finalMappings) // Wysłanie danych jako JSON
    })
        .then(r => r.json()) // Odczytanie odpowiedzi jako JSON
        .then(d => {
            if (d.success !== undefined) { // Sprawdź czy odpowiedź ma pole success
                alert(d.message); // Wyświetl komunikat z backendu
            } else {
                // Jeśli format odpowiedzi jest inny, np. tylko tekst
                alert(d);
            }
        })
        .catch(err => {
            console.error("Błąd zapisu mapowań:", err);
            alert("Wystąpił błąd podczas zapisywania mapowań.");
        });
});

// Listener dla przycisku "Odśwież Mapowania z Bazy"
document.getElementById("reloadMappings").addEventListener("click", () => {
    fetch(`/CeneoImportWizardXml/GetCeneoMappings?storeId=${storeId}`) // Endpoint dla Ceneo
        .then(r => r.json())
        .then(data => {
            console.log("Odebrano mapowania z bazy (reload):", data);
            clearAllHighlights(); // Usunięcie starych podświetleń

            // Zresetowanie obiektu mapowań
            Object.keys(mappingForField).forEach(f => mappingForField[f] = null);
            // Wczytanie nowo pobranych mapowań
            data.forEach(m => {
                if (m.fieldName && mappingForField.hasOwnProperty(m.fieldName)) {
                    mappingForField[m.fieldName] = { xpath: m.localName, nodeCount: 0, firstValue: "" };
                }
            });
            // Zastosowanie i podświetlenie nowych mapowań
            applyExistingMappings();
        })
        .catch(err => {
            console.error("Błąd getCeneoMappings:", err);
            alert("Wystąpił błąd podczas pobierania mapowań z bazy.");
        });
});


// --- ZMIANA: Zaktualizowana funkcja extractProductsFromXml ---
// Listener dla przycisku "Wyciągnij Produkty z XML"
document.getElementById("extractProducts").addEventListener("click", () => {
    // Sprawdzenie, czy XML został poprawnie załadowany
    if (!xmlDoc) {
        alert("Brak XML do parsowania. Sprawdź, czy plik został poprawnie załadowany.");
        return;
    }

    // Próba automatycznego wykrycia głównego tagu produktu
    let entries = [];
    const possibleProductTags = ["o", "item", "entry", "produkt", "product"]; // Lista możliwych tagów
    let detectedTag = null;

    for (const tag of possibleProductTags) {
        const nodes = xmlDoc.getElementsByTagName(tag);
        if (nodes.length > 0) {
            entries = nodes;
            detectedTag = tag;
            console.log(`Wykryto tag produktu: <${detectedTag}> (${entries.length} elementów)`);
            break; // Znaleziono pierwszy pasujący tag, użyj go
        }
    }

    if (entries.length === 0) {
        alert(`Nie znaleziono żadnego ze standardowych tagów produktu (${possibleProductTags.join(', ')}). Sprawdź strukturę XML.`);
        return;
    }

    // Odczytanie opcji filtrowania z checkboxów
    const onlyEanProducts = document.getElementById("onlyEanProducts")?.checked ?? false;
    const removeParams = document.getElementById("cleanUrlParameters")?.checked ?? false;
    const removeDuplicateUrls = document.getElementById("removeDuplicateUrls")?.checked ?? false;
    const removeDuplicateEans = document.getElementById("removeDuplicateEans")?.checked ?? false;

    let productMaps = []; // Tablica na wyekstrahowane dane produktów
    let countUrlsWithParams = 0; // Licznik URL-i z parametrami

    // Iteracja przez znalezione węzły produktów
    for (let i = 0; i < entries.length; i++) {
        let entryNode = entries[i]; // Bieżący węzeł produktu
        // Stworzenie obiektu DTO dla produktu
        let pm = {
            StoreId: storeId, // Konwersja na string dla spójności, backend sparsuje
            ExternalId: getVal(entryNode, "ExternalId"),
            Url: getVal(entryNode, "Url"),
            CeneoEan: getVal(entryNode, "CeneoEan"),
            CeneoImage: getVal(entryNode, "CeneoImage"),
            CeneoExportedName: getVal(entryNode, "CeneoExportedName"),
            // --- DODANE: Pobranie i sparsowanie cen ---
            CeneoXMLPrice: parsePrice(getVal(entryNode, "CeneoXMLPrice")),
            CeneoDeliveryXMLPrice: parsePrice(getVal(entryNode, "CeneoDeliveryXMLPrice"))
            // --- KONIEC DODAWANIA ---
        };

        // Opcjonalne filtrowanie: tylko produkty z EAN
        if (onlyEanProducts && (!pm.CeneoEan || !pm.CeneoEan.trim())) {
            continue; // Pomiń produkt, jeśli nie ma EAN
        }

        // Opcjonalne czyszczenie parametrów URL
        if (pm.Url) {
            let qIdx = pm.Url.indexOf('?');
            if (qIdx !== -1) {
                countUrlsWithParams++; // Zlicz URL z parametrami
                if (removeParams) {
                    pm.Url = pm.Url.substring(0, qIdx); // Usuń parametry
                }
            }
        }
        productMaps.push(pm); // Dodaj przetworzony produkt do listy
    }

    console.log("Surowe productMaps (przed deduplikacją):", productMaps);

    // Opcjonalna deduplikacja po URL
    if (removeDuplicateUrls) {
        let seenUrls = new Set();
        productMaps = productMaps.filter(pm => {
            if (!pm.Url || !pm.Url.trim()) return true; // Zachowaj produkty bez URL? (do decyzji)
            if (seenUrls.has(pm.Url)) {
                return false; // URL już widziany, odrzuć duplikat
            }
            seenUrls.add(pm.Url); // Dodaj nowy URL do zbioru widzianych
            return true; // Zachowaj ten produkt
        });
    }

    // Opcjonalna deduplikacja po EAN
    if (removeDuplicateEans) {
        let seenEans = new Set();
        productMaps = productMaps.filter(pm => {
            if (!pm.CeneoEan || !pm.CeneoEan.trim()) return true; // Zachowaj produkty bez EAN? (do decyzji)
            if (seenEans.has(pm.CeneoEan)) {
                return false; // EAN już widziany, odrzuć duplikat
            }
            seenEans.add(pm.CeneoEan); // Dodaj nowy EAN do zbioru widzianych
            return true; // Zachowaj ten produkt
        });
    }

    console.log("Finalne productMaps (po filtrach i deduplikacji):", productMaps);

    // Wyświetlenie podglądu JSON w textarea
    const previewElement = document.getElementById("productMapsPreview");
    if (previewElement) {
        previewElement.textContent = JSON.stringify(productMaps, null, 2); // Formatowany JSON
    }

    // Wyświetlenie statystyk
    const urlParamsInfo = document.getElementById("urlParamsInfo");
    if (urlParamsInfo) {
        urlParamsInfo.textContent = "Liczba URL zawierających parametry: " + countUrlsWithParams;
    }
    const finalNodesInfo = document.getElementById("finalNodesInfo");
    if (finalNodesInfo) {
        finalNodesInfo.textContent = "Finalna liczba produktów po filtrach: " + productMaps.length;
    }

    // Sprawdzenie i wyświetlenie informacji o duplikatach (po ewentualnej deduplikacji)
    checkDuplicates(productMaps);
});
// --- KONIEC ZMIANY ---


// --- ZMIANA: Zaktualizowana funkcja getVal ---
// Funkcja pobierająca wartość z węzła XML na podstawie nazwy pola i zmapowanego XPath
function getVal(entryNode, fieldName) {
    const info = mappingForField[fieldName]; // Pobierz info o mapowaniu dla danego pola
    if (!info || !info.xpath) return null; // Jeśli brak mapowania, zwróć null

    let path = info.xpath; // XPath z mapowania

    // --- Bardziej elastyczne usuwanie prefiksu ścieżki bazowej ---
    // Zakładamy, że `entryNode` to główny węzeł produktu (np. <o>, <item>)
    // Chcemy uzyskać ścieżkę *względną* do tego węzła.
    const entryNodePath = "/" + entryNode.nodeName.toLowerCase(); // np. "/o", "/item"

    // Znajdź ścieżkę do rodzica entryNode (jeśli istnieje)
    let parentPath = "";
    let current = entryNode.parentNode;
    while (current && current.nodeType === Node.ELEMENT_NODE) {
        // Prosty sposób - może być niedokładny przy złożonych strukturach
        parentPath = "/" + current.nodeName.toLowerCase() + parentPath;
        current = current.parentNode;
    }
    // Pełna ścieżka bazowa do entryNode (przybliżona)
    const basePath = parentPath + entryNodePath; // np. "/rss/channel/item", "/offers/o"

    // Jeśli XPath z mapowania zaczyna się od tej ścieżki bazowej, usuń ją
    if (path.toLowerCase().startsWith(basePath)) {
        // Długość może być inna z powodu predykatów [@name='...']
        // Znajdź rzeczywisty punkt startowy ścieżki względnej
        const relativePathStart = path.toLowerCase().indexOf(entryNodePath) + entryNodePath.length;
        path = path.substring(relativePathStart);
    }
    // Jeśli ścieżka z mapowania nie zaczyna się od basePath, ale zaczyna się od "/",
    // może to być błąd mapowania lub ścieżka do atrybutu węzła nadrzędnego.
    // Usuwamy wiodący "/" aby traktować ją jako względną od entryNode.
    else if (path.startsWith("/")) {
        path = path.substring(1);
    }
    // Jeśli ścieżka nie zaczyna się od "/", zakładamy, że jest już względna.

    // --- Koniec usuwania prefiksu ---

    let currentNode = entryNode; // Zaczynamy od głównego węzła produktu
    const segments = path.split('/').filter(Boolean); // Podziel ścieżkę na segmenty, usuń puste

    for (let i = 0; i < segments.length; i++) {
        let seg = segments[i];

        // Przypadek końcowy: wartość tekstowa (#value)
        if (seg === '#value') {
            // Zwróć zawartość tekstową bieżącego węzła
            return currentNode.textContent ? currentNode.textContent.trim() : null;
        }

        // Przypadek końcowy: atrybut (@attrName)
        if (seg.startsWith('@')) {
            const attrName = seg.substring(1); // Nazwa atrybutu
            return currentNode.getAttribute(attrName); // Zwróć wartość atrybutu
        }

        // Nawigacja do następnego węzła potomnego

        // Obsługa predykatu [@name="..."] (np. a[@name="EAN"])
        const bracketPos = seg.indexOf('[@name="');
        let child = null;
        if (bracketPos !== -1) {
            const baseName = seg.substring(0, bracketPos); // Nazwa tagu (np. 'a')
            const inside = seg.substring(bracketPos + 8);
            const endBracket = inside.indexOf('"]');
            if (endBracket === -1) return null; // Błędny format predykatu
            const desiredName = inside.substring(0, endBracket); // Wartość atrybutu name

            // Znajdź dziecko o pasującym tagu i atrybucie name
            child = Array.from(currentNode.children).find(ch =>
                ch.localName === baseName && ch.getAttribute("name") === desiredName
            );
        } else {
            // Standardowe szukanie dziecka po nazwie tagu (ignorując przestrzeń nazw)
            const tagName = seg.includes(':') ? seg.split(':')[1] : seg; // np. g:id -> id
            child = Array.from(currentNode.children).find(ch => ch.localName === tagName);
        }

        if (!child) return null; // Nie znaleziono pasującego dziecka, ścieżka nie istnieje
        currentNode = child; // Przejdź do znalezionego dziecka
    }

    // Jeśli pętla się zakończyła, oznacza to, że ścieżka wskazywała na element (a nie atrybut/#value)
    // Zwracamy zawartość tekstową tego ostatniego elementu
    return currentNode.textContent ? currentNode.textContent.trim() : null;
}
// --- KONIEC ZMIANY ---


// Listener dla przycisku "Zapisz Wyciągnięte Produkty w Bazie"
document.getElementById("saveProductMapsInDb").addEventListener("click", () => {
    const previewElement = document.getElementById("productMapsPreview");
    if (!previewElement) {
        alert("Brak elementu podglądu productMapsPreview!");
        return;
    }
    const txt = previewElement.textContent.trim();
    if (!txt) {
        alert("Brak danych produktów (productMaps) do zapisania! Kliknij najpierw 'Wyciągnij Produkty'.");
        return;
    }
    let productMaps;
    try {
        productMaps = JSON.parse(txt); // Sparsuj JSON z podglądu
    } catch (e) {
        alert("Błędny format JSON w podglądzie productMapsPreview!");
        console.error("Błąd parsowania JSON:", e, "\nTekst:", txt);
        return;
    }

    if (!Array.isArray(productMaps)) {
        alert("Dane w podglądzie nie są poprawną tablicą produktów!");
        return;
    }

    console.log("Wysyłanie productMaps do zapisu:", productMaps);

    // Wysłanie danych do endpointu backendowego Ceneo
    fetch("/CeneoImportWizardXml/SaveProductMapsFromFront", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(productMaps) // Wyślij sparsowane dane
    })
        .then(r => r.json()) // Oczekujemy odpowiedzi JSON
        .then(d => {
            // Wyświetl komunikat sukcesu lub błędu z backendu
            if (d.success !== undefined) {
                alert((d.success ? "Sukces: " : "Błąd: ") + d.message);
            } else {
                alert("Otrzymano nieoczekiwaną odpowiedź z serwera.");
                console.log("Odpowiedź serwera:", d);
            }
        })
        .catch(err => {
            console.error("Błąd zapisu ProductMaps:", err);
            alert("Wystąpił błąd sieci lub serwera podczas zapisu produktów.");
        });
});

// Funkcja sprawdzająca duplikaty URL i EAN w finalnej liście produktów
function checkDuplicates(productMaps) {
    if (!Array.isArray(productMaps)) {
        console.error("checkDuplicates: wejście nie jest tablicą");
        return;
    }
    const urlCounts = {};
    const eanCounts = {};
    let productsWithoutUrl = 0;
    let productsWithoutEan = 0;

    // Zlicz wystąpienia każdego URL i EAN
    productMaps.forEach(pm => {
        if (pm.Url && pm.Url.trim()) {
            urlCounts[pm.Url] = (urlCounts[pm.Url] || 0) + 1;
        } else {
            productsWithoutUrl++;
        }
        if (pm.CeneoEan && pm.CeneoEan.trim()) {
            eanCounts[pm.CeneoEan] = (eanCounts[pm.CeneoEan] || 0) + 1;
        } else {
            productsWithoutEan++;
        }
    });

    // Oblicz liczbę unikalnych i zduplikowanych URL/EAN
    const totalUniqueUrls = Object.keys(urlCounts).length;
    const duplicateUrlsCount = Object.values(urlCounts).filter(count => count > 1).length;

    const totalUniqueEans = Object.keys(eanCounts).length;
    const duplicateEansCount = Object.values(eanCounts).filter(count => count > 1).length;

    // Przygotuj komunikaty
    let urlMessage = `Unikalnych URL: ${totalUniqueUrls}`;
    if (duplicateUrlsCount > 0) {
        urlMessage += ` <span style="color:red;">(Znaleziono ${duplicateUrlsCount} zduplikowanych wartości URL)</span>`;
    } else {
        urlMessage += ` (Brak duplikatów)`;
    }
    if (productsWithoutUrl > 0) {
        urlMessage += ` | Produktów bez URL: ${productsWithoutUrl}`;
    }

    let eanMessage = `Unikalnych EAN: ${totalUniqueEans}`;
    if (duplicateEansCount > 0) {
        eanMessage += ` <span style="color:red;">(Znaleziono ${duplicateEansCount} zduplikowanych wartości EAN)</span>`;
    } else {
        eanMessage += ` (Brak duplikatów)`;
    }
    if (productsWithoutEan > 0) {
        eanMessage += ` | Produktów bez EAN: ${productsWithoutEan}`;
    }


    // Wyświetl informacje w odpowiednim elemencie HTML
    const duplicatesInfoElement = document.getElementById("duplicatesInfo");
    if (duplicatesInfoElement) {
        duplicatesInfoElement.innerHTML = urlMessage + "<br>" + eanMessage;
    }
}

// Listenery dla checkboxów - automatycznie uruchamiają ekstrakcję po zmianie
const cleanUrlCheckbox = document.getElementById("cleanUrlParameters");
if (cleanUrlCheckbox) {
    cleanUrlCheckbox.addEventListener("change", () => {
        // Symuluj kliknięcie przycisku ekstrakcji, aby odświeżyć podgląd
        document.getElementById("extractProducts")?.click();
    });
}
const removeDupUrlsCheckbox = document.getElementById("removeDuplicateUrls");
if (removeDupUrlsCheckbox) {
    removeDupUrlsCheckbox.addEventListener("change", () => {
        document.getElementById("extractProducts")?.click();
    });
}
const removeDupEansCheckbox = document.getElementById("removeDuplicateEans");
if (removeDupEansCheckbox) {
    removeDupEansCheckbox.addEventListener("change", () => {
        document.getElementById("extractProducts")?.click();
    });
}
const onlyEanCheckbox = document.getElementById("onlyEanProducts");
if (onlyEanCheckbox) {
    onlyEanCheckbox.addEventListener("change", () => {
        document.getElementById("extractProducts")?.click();
    });
}


// Inicjalne renderowanie tabeli mapowań po załadowaniu strony
renderMappingTable();