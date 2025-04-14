
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


function createLi(node, parentPath) {
    let li = document.createElement("li");
    li.classList.add("xml-node");
    let nodeName = node.nodeName;
    let nameAttr = node.getAttribute && node.getAttribute("name");
    if (nameAttr) {
        nodeName += `[@name="${nameAttr}"]`;
    }
    let currentPath = parentPath ? parentPath + "/" + nodeName : "/" + nodeName;
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
    // Jeśli brak dzieci, ale jest tekst => #value
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


function queryNodesByXPath(xPathValue) {
    let esc = escapeForSelector(xPathValue);
    return document.querySelectorAll(`.xml-node[data-xpath="${esc}"]`);
}
function escapeForSelector(val) {
    return val.replace(/\\/g, "\\\\").replace(/"/g, '\\"');
}


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
                    mappingForField[m.fieldName] = { xpath: m.localName, nodeCount: 0, firstValue: "" };
                }
            });
            applyExistingMappings();
        })
        .catch(err => console.error("Błąd getCeneoMappings:", err));
});

document.getElementById("extractProducts").addEventListener("click", () => {
    if (!xmlDoc) {
        alert("Brak XML do parsowania");
        return;
    }
    // Nowa zmienna: czy pomijamy produkty bez EAN?
    let onlyEanProducts = document.getElementById("onlyEanProducts").checked;

    for (let i = 0; i < entries.length; i++) {
        let en = entries[i];
        let pm = {
            StoreId: storeId.toString(),
            ExternalId: getVal(en, "ExternalId"),
            Url: getVal(en, "Url"),
            CeneoEan: getVal(en, "CeneoEan"),
            CeneoImage: getVal(en, "CeneoImage"),
            CeneoExportedName: getVal(en, "CeneoExportedName")
        };

        // Jeśli "onlyEanProducts" = true, a EAN jest pusty, to pomijamy
        if (onlyEanProducts && (!pm.CeneoEan || !pm.CeneoEan.trim())) {
            continue;
        }

        productMaps.push(pm);
    }

    // Wyświetlamy surowy JSON przed filtrami
    document.getElementById("productMapsPreview").textContent = JSON.stringify(productMaps, null, 2);

  
    let removeParams = document.getElementById("cleanUrlParameters").checked;
    let countUrlsWithParams = 0;
    productMaps.forEach(pm => {
        if (pm.Url) {
            let qIdx = pm.Url.indexOf('?');
            if (qIdx !== -1) {
                countUrlsWithParams++;
                if (removeParams) {
                    pm.Url = pm.Url.substring(0, qIdx);
                }
            }
        }
    });
    document.getElementById("urlParamsInfo").textContent = "Liczba URL zawierających parametry: " + countUrlsWithParams;

    // Dedupikacja URL (jeśli zaznaczono)
    if (document.getElementById("removeDuplicateUrls").checked) {
        let seen = {};
        productMaps = productMaps.filter(pm => {
            if (pm.Url) {
                if (seen[pm.Url]) {
                    return false;
                } else {
                    seen[pm.Url] = true;
                    return true;
                }
            }
            return true;
        });
    }
    // Dedupikacja EAN (jeśli zaznaczono)
    if (document.getElementById("removeDuplicateEans").checked) {
        let seenEan = {};
        productMaps = productMaps.filter(pm => {
            if (pm.CeneoEan) {
                if (seenEan[pm.CeneoEan]) {
                    return false;
                } else {
                    seenEan[pm.CeneoEan] = true;
                    return true;
                }
            }
            return true;
        });
    }
    // Aktualizacja licznika finalnych węzłów
    document.getElementById("finalNodesInfo").textContent = "Final nodes po filtrach: " + productMaps.length;
    // Aktualizacja podglądu JSON
    document.getElementById("productMapsPreview").textContent = JSON.stringify(productMaps, null, 2);
    // Sprawdzenie duplikatów (na finalnej liście)
    checkDuplicates(productMaps);
});





function getVal(entryNode, fieldName) {
    let info = mappingForField[fieldName];
    if (!info || !info.xpath) return null;
    let path = info.xpath;

    // Usuń prefiksy związane ze strukturą XML, gdyż entryNode jest elementem <o>
    if (path.startsWith("/offers/o")) {
        path = path.replace("/offers/o", "");
    } else if (path.startsWith("/offers/group")) {
        // Usuń /offers/group[@name="..."]/o – dopasowanie do dowolnej wartości atrybutu name
        path = path.replace(/\/offers\/group\[@name="[^"]+"\]\/o/, "");
    } else if (path.startsWith("/offers")) {
        path = path.replace("/offers", "");
    }

    if (!path.startsWith("/")) {
        path = "/" + path;
    }

    // Podziel ścieżkę na segmenty
    let segments = path.split("/").filter(Boolean);
    let currentNode = entryNode;

    for (let seg of segments) {
        if (seg === "#value") {
            return currentNode.textContent.trim();
        }
        if (seg.startsWith("@")) {
            return currentNode.getAttribute(seg.slice(1));
        }
        // Jeśli segment zawiera filtr według atrybutu name, np. a[@name="EAN"]
        let bracketPos = seg.indexOf('[@name="');
        if (bracketPos !== -1) {
            let baseName = seg.substring(0, bracketPos);
            let inside = seg.substring(bracketPos + 8);
            let endBracket = inside.indexOf('"]');
            let desiredName = inside.substring(0, endBracket);
            let child = Array.from(currentNode.children).find(ch =>
                ch.localName === baseName && ch.getAttribute("name") === desiredName
            );
            if (!child) return null;
            currentNode = child;
        } else {
            // W przeciwnym razie szukamy dziecka o danej nazwie
            let child = Array.from(currentNode.children).find(ch =>
                ch.localName === seg
            );
            if (!child) return null;
            currentNode = child;
        }
    }
    return currentNode.textContent.trim();
}



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


function checkDuplicates(productMaps) {
    let urlCounts = {};
    let eanCounts = {};
    productMaps.forEach(pm => {
        if (pm.Url) {
            urlCounts[pm.Url] = (urlCounts[pm.Url] || 0) + 1;
        }
        if (pm.CeneoEan) {
            eanCounts[pm.CeneoEan] = (eanCounts[pm.CeneoEan] || 0) + 1;
        }
    });
    let totalUniqueUrls = Object.keys(urlCounts).length;
    let duplicateUrls = 0;
    for (let key in urlCounts) {
        if (urlCounts[key] > 1) {
            duplicateUrls++;
        }
    }
    let totalUniqueEans = Object.keys(eanCounts).length;
    let duplicateEans = 0;
    for (let key in eanCounts) {
        if (eanCounts[key] > 1) {
            duplicateEans++;
        }
    }
    let urlMessage = `Unikalnych URL: ${totalUniqueUrls} `;
    urlMessage += duplicateUrls > 0
        ? `<span style="color:red;">(Duplikatów: ${duplicateUrls})</span>`
        : `(Brak duplikatów)`;
    let eanMessage = `Unikalnych kodów EAN: ${totalUniqueEans} `;
    eanMessage += duplicateEans > 0
        ? `<span style="color:red;">(Duplikatów: ${duplicateEans})</span>`
        : `(Brak duplikatów)`;
    document.getElementById("duplicatesInfo").innerHTML = urlMessage + "<br>" + eanMessage;
}


document.getElementById("cleanUrlParameters").addEventListener("change", () => {
    document.getElementById("extractProducts").click();
});
document.getElementById("removeDuplicateUrls").addEventListener("change", () => {
    document.getElementById("extractProducts").click();
});
document.getElementById("removeDuplicateEans").addEventListener("change", () => {
    document.getElementById("extractProducts").click();
});


renderMappingTable();