//////////////////////////////
// 1. Pobieranie parametrów //
//////////////////////////////

const ceneoDataEl = document.getElementById("ceneo-wizard-data");
const storeId = parseInt(ceneoDataEl.getAttribute("data-store-id") || "0", 10);
let existingMappingsJson = ceneoDataEl.getAttribute("data-existing-mappings") || "[]";
let existingMappings = [];

try {
    existingMappings = JSON.parse(existingMappingsJson);
} catch {
    existingMappings = [];
}

let mappingForField = {
    ExternalId: null,
    Url: null,
    CeneoEan: null,
    CeneoImage: null,
    CeneoExportedName: null
};

existingMappings.forEach(m => {
    if (m.fieldName) {
        mappingForField[m.fieldName] = {
            xpath: m.localName,
            nodeCount: 0,
            firstValue: ""
        };
    }
});

let xmlDoc = null;
let proxyUrl = `/CeneoImportWizardXml/ProxyXml?storeId=${storeId}`;

//////////////////////////////
// 2. Pobieranie XML       //
//////////////////////////////

fetch(proxyUrl)
    .then(resp => resp.text())
    .then(xmlStr => {
        let parser = new DOMParser();
        xmlDoc = parser.parseFromString(xmlStr, "application/xml");
        if (xmlDoc.documentElement.nodeName === "parsererror") {
            document.getElementById("xmlContainer").innerText =
                "Błąd parsowania XML:\n" + xmlDoc.documentElement.textContent;
            return;
        }
        buildXmlTree(xmlDoc.documentElement, "");
        applyExistingMappings();
    })
    .catch(err => {
        document.getElementById("xmlContainer").innerText =
            "Błąd pobierania XML:\n" + err;
    });

//////////////////////////////
// 3. Budowanie drzewa     //
//////////////////////////////

function buildXmlTree(node, parentPath) {
    let container = document.getElementById("xmlContainer");
    if (!parentPath && !container.querySelector("ul")) {
        let ulRoot = document.createElement("ul");
        ulRoot.appendChild(createLi(node, parentPath));
        container.appendChild(ulRoot);
    } else {
        let rootUl = container.querySelector("ul");
        rootUl.appendChild(createLi(node, parentPath));
    }
}

/**
 * createLi(node, parentPath):
 * - Tworzy <li> z data-xpath (np. "/o/cat")
 * - Jeśli węzeł ma atrybut name="EAN", dopisuje [@name="EAN"]
 * - Jeśli brak dzieci, a jest textContent -> tworzy sub-węzeł "#value"
 */
function createLi(node, parentPath) {
    let li = document.createElement("li");
    li.classList.add("xml-node");

    let nodeName = node.nodeName;
    let nameAttr = node.getAttribute && node.getAttribute("name");
    if (nameAttr) {
        nodeName += `[@name="${nameAttr}"]`;
    }

    let currentPath = parentPath ? (parentPath + "/" + nodeName) : ("/" + nodeName);
    li.setAttribute("data-xpath", currentPath);

    let b = document.createElement("b");
    b.innerText = node.nodeName + (nameAttr ? ` (name="${nameAttr}")` : "");
    li.appendChild(b);

    // Atrybuty
    if (node.attributes && node.attributes.length > 0) {
        let ulAttrs = document.createElement("ul");
        Array.from(node.attributes).forEach(attr => {
            let attrLi = document.createElement("li");
            attrLi.classList.add("xml-node");
            let attrPath = currentPath + `/@${attr.name}`;
            attrLi.setAttribute("data-xpath", attrPath);

            let attrLabel = document.createElement("span");
            attrLabel.innerText = attr.value;
            attrLi.appendChild(attrLabel);

            ulAttrs.appendChild(attrLi);
        });
        li.appendChild(ulAttrs);
    }

    // Jeżeli brak dzieci, ale jest tekst => #value
    let textVal = node.textContent.trim();
    if (node.children.length === 0 && textVal) {
        let ulVal = document.createElement("ul");
        let liVal = document.createElement("li");
        liVal.classList.add("xml-node");

        let valPath = currentPath + "/#value";
        liVal.setAttribute("data-xpath", valPath);

        let spanVal = document.createElement("span");
        spanVal.innerText = textVal;
        liVal.appendChild(spanVal);

        ulVal.appendChild(liVal);
        li.appendChild(ulVal);
    }

    if (node.children.length > 0) {
        let ul = document.createElement("ul");
        Array.from(node.children).forEach(child => {
            ul.appendChild(createLi(child, currentPath));
        });
        li.appendChild(ul);
    }

    return li;
}

//////////////////////////////
// 4. Klikanie w drzewie   //
//////////////////////////////

document.addEventListener("click", e => {
    let el = e.target.closest(".xml-node");
    if (!el) return;

    let selectedField = document.getElementById("fieldSelector").value;
    let xPath = el.getAttribute("data-xpath");

    // Usunięcie poprzednich highlightów
    document.querySelectorAll(`.highlight-${selectedField}`)
        .forEach(x => x.classList.remove(`highlight-${selectedField}`));

    let sameNodes = queryNodesByXPath(xPath);
    sameNodes.forEach(n => n.classList.add(`highlight-${selectedField}`));

    let count = sameNodes.length;
    let firstVal = "";
    if (count > 0) {
        let sp = sameNodes[0].querySelector("span");
        if (sp) {
            firstVal = sp.innerText.replace(/^(\s*:\s*)/, "").trim();
        }
    }
    mappingForField[selectedField] = { xpath: xPath, nodeCount: count, firstValue: firstVal };
    renderMappingTable();
});

//////////////////////////////
// 5. querySelector z escapowaniem
//////////////////////////////

function queryNodesByXPath(xPathValue) {
    let esc = escapeForSelector(xPathValue);
    return document.querySelectorAll(`.xml-node[data-xpath="${esc}"]`);
}

function escapeForSelector(val) {
    return val.replace(/\\/g, "\\\\").replace(/"/g, '\\"');
}

//////////////////////////////
// 6. Kolorowanie wg mapowań
//////////////////////////////

function applyExistingMappings() {
    for (let fieldName in mappingForField) {
        let info = mappingForField[fieldName];
        if (!info || !info.xpath) continue;

        let sameNodes = queryNodesByXPath(info.xpath);
        let count = sameNodes.length;
        let firstVal = "";
        if (count > 0) {
            let sp = sameNodes[0].querySelector("span");
            if (sp) {
                firstVal = sp.innerText.replace(/^(\s*:\s*)/, "").trim();
            }
        }
        info.nodeCount = count;
        info.firstValue = firstVal;
        sameNodes.forEach(n => n.classList.add(`highlight-${fieldName}`));
    }
    renderMappingTable();
}

function clearAllHighlights() {
    Object.keys(mappingForField).forEach(field => {
        document.querySelectorAll(`.highlight-${field}`)
            .forEach(el => el.classList.remove(`highlight-${field}`));
    });
}

//////////////////////////////
// 7. Render tabeli mapowań
//////////////////////////////

function renderMappingTable() {
    let tbody = document.getElementById("mappingTable").querySelector("tbody");
    tbody.innerHTML = "";
    for (let fieldName in mappingForField) {
        let info = mappingForField[fieldName];
        let tr = document.createElement("tr");
        if (!info) {
            tr.innerHTML = `<td>${fieldName}</td><td>-</td><td>0</td><td>-</td>`;
        } else {
            tr.innerHTML = `
                <td>${fieldName}</td>
                <td>${info.xpath || "-"}</td>
                <td>${info.nodeCount || 0}</td>
                <td>${info.firstValue || "-"}</td>
            `;
        }
        tbody.appendChild(tr);
    }
}

//////////////////////////////
// 8. Zapis mapowania
//////////////////////////////

document.getElementById("saveMapping").addEventListener("click", () => {
    let finalMappings = [];
    for (let fieldName in mappingForField) {
        let info = mappingForField[fieldName];
        if (info && info.xpath) {
            finalMappings.push({ fieldName, localName: info.xpath });
        }
    }
    fetch(`/CeneoImportWizardXml/SaveCeneoMappings?storeId=${storeId}`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(finalMappings)
    })
        .then(r => r.json())
        .then(d => alert(d.message))
        .catch(err => console.error(err));
});

document.getElementById("reloadMappings").addEventListener("click", () => {
    fetch(`/CeneoImportWizardXml/GetCeneoMappings?storeId=${storeId}`)
        .then(r => r.json())
        .then(data => {
            clearAllHighlights();
            Object.keys(mappingForField).forEach(f => mappingForField[f] = null);
            data.forEach(m => {
                if (m.fieldName) {
                    mappingForField[m.fieldName] = {
                        xpath: m.localName, nodeCount: 0, firstValue: ""
                    };
                }
            });
            applyExistingMappings();
        })
        .catch(err => console.error("Błąd getCeneoMappings:", err));
});

//////////////////////////////
// 9. Wyciąganie produktów  //
//////////////////////////////

document.getElementById("extractProducts").addEventListener("click", () => {
    if (!xmlDoc) {
        console.log("extractProducts: xmlDoc is null/undefined");
        alert("Brak XML do parsowania");
        return;
    }

    let entries = xmlDoc.getElementsByTagName("o");
    console.log("extractProducts: found <o> nodes =", entries.length);

    let productMaps = [];
    for (let i = 0; i < entries.length; i++) {
        let en = entries[i];
        console.log(`Entry #${i}, <o> with id=`, en.getAttribute("id"));

        // Mapa dla jednego <o>
        let pm = {
            StoreId: storeId.toString(),
            ExternalId: getVal(en, "ExternalId"),
            Url: getVal(en, "Url"),
            CeneoEan: getVal(en, "CeneoEan"),
            CeneoImage: getVal(en, "CeneoImage"),
            CeneoExportedName: getVal(en, "CeneoExportedName")
        };

        console.log(`   Mapped data for entry #${i}:`, pm);

        // Przykładowo odfiltrowujemy te, które nie mają EAN
        if (!pm.CeneoEan || !pm.CeneoEan.trim()) {
            console.log(`   Skipping entry #${i}, EAN is empty ->`, pm.CeneoEan);
            continue;
        }
        productMaps.push(pm);
    }

    console.log("productMaps(front):", productMaps);
    document.getElementById("productMapsPreview").textContent =
        JSON.stringify(productMaps, null, 2);
});

//////////////////////////////
//10. getVal (obsługa #value)
//////////////////////////////

function getVal(entryNode, fieldName) {
    let info = mappingForField[fieldName];
    if (!info || !info.xpath) {
        console.log(`getVal: No mapping info for field=${fieldName}`);
        return null;
    }

    let path = info.xpath;
    console.log(`getVal: field=${fieldName}, path=${path}`);

    // Nowy fragment: usuwamy prefiksy "/offers" lub "/offers/o" 
    // bo w extractProducts i tak startujemy od <o> 
    if (path.startsWith("/offers/o")) {
        path = path.replace("/offers/o", "");
    } else if (path.startsWith("/offers")) {
        path = path.replace("/offers", "");
    }

    // Jeśli ścieżka została pusta -> np. "/offers/o/@id" -> "/@id"
    // Upewniamy się, że zaczyna się od "/"
    if (!path.startsWith("/")) {
        path = "/" + path;
    }

    console.log(`getVal: after prefix removal => ${path}`);

    let segments = path.split("/").filter(Boolean);
    let currentNode = entryNode;

    for (let seg of segments) {
        console.log(`  Segment="${seg}", currentNode=<${currentNode.localName} id=${currentNode.getAttribute("id")}>`);

        if (seg === "#value") {
            let val = currentNode.textContent.trim();
            console.log("  #value =>", val);
            return val;
        }
        if (seg.startsWith("@")) {
            let attrName = seg.slice(1);
            let attrVal = currentNode.getAttribute(attrName);
            console.log(`  @attribute => ${attrName} =`, attrVal);
            return attrVal;
        }
        let bracketPos = seg.indexOf('[@name="');
        if (bracketPos !== -1) {
            let baseName = seg.substring(0, bracketPos);
            let inside = seg.substring(bracketPos + 8);
            let endBracket = inside.indexOf('"]');
            let desiredName = inside.substring(0, endBracket);

            console.log(`  Searching child <${baseName} name="${desiredName}"> among ${currentNode.children.length} children`);

            let child = Array.from(currentNode.children).find(ch =>
                ch.localName === baseName && ch.getAttribute("name") === desiredName
            );
            if (!child) {
                console.log("    Not found => returning null");
                return null;
            }
            currentNode = child;
        } else {
            // zwykły segment, np. "attrs" czy "imgs"
            console.log(`  Searching child <${seg}> among ${currentNode.children.length} children`);
            let child = Array.from(currentNode.children).find(ch =>
                ch.localName === seg
            );
            if (!child) {
                console.log(`    Not found <${seg}> => returning null`);
                return null;
            }
            currentNode = child;
        }
    }

    let finalVal = currentNode.textContent.trim();
    console.log("  end of path => textContent=", finalVal);
    return finalVal;
}

//////////////////////////////
//11. Zapis do bazy
//////////////////////////////

document.getElementById("saveProductMapsInDb").addEventListener("click", () => {
    let txt = document.getElementById("productMapsPreview").textContent.trim();
    if (!txt) {
        alert("Brak productMaps do zapisania!");
        return;
    }
    let productMaps;
    try {
        productMaps = JSON.parse(txt);
    } catch {
        alert("Błędny JSON w productMapsPreview!");
        return;
    }
    fetch("/CeneoImportWizardXml/SaveProductMapsFromFront", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(productMaps)
    })
        .then(r => r.json())
        .then(d => alert("Zapisano: " + d.message))
        .catch(err => console.error(err));
});

// Na start: renderuj tabelę
renderMappingTable();
