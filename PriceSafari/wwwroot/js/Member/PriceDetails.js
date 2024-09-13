document.addEventListener("DOMContentLoaded", function () {
    // Pobierz storeName z atrybutu data w elemencie Vert
    var observedStoreName = document.querySelector(".Vert").getAttribute("data-store-name").toLowerCase();
    var rows = Array.from(document.querySelectorAll(".price-row"));

    // Podświetlanie obserwowanego sklepu
    rows.forEach(function (row) {
        var storeName = row.getAttribute("data-store-name").toLowerCase();
        if (storeName === observedStoreName) {
            row.querySelector(".store-name").style.fontWeight = "bold";
            row.querySelector(".store-price").style.fontWeight = "bold";
        }
    });

    // Sortowanie po cenie
    function sortRowsByPrice() {
        rows.sort((a, b) => parseFloat(a.getAttribute("data-price")) - parseFloat(b.getAttribute("data-price")));
        rows.forEach(row => document.querySelector("#priceTable tbody").appendChild(row));
    }

    // Sortowanie po pozycji, gdzie null jest na końcu
    function sortRowsByPosition() {
        rows.sort((a, b) => {
            var posA = parseInt(a.getAttribute("data-position")) || Infinity; 
            var posB = parseInt(b.getAttribute("data-position")) || Infinity;
            return posA - posB;
        });
        rows.forEach(row => document.querySelector("#priceTable tbody").appendChild(row));
    }

    // Obsługa zmiany sortowania
    function handleSortChange() {
        if (this.id === "sortPrice" && this.checked) {
            document.getElementById("sortPosition").checked = false;
            sortRowsByPrice();
        } else if (this.id === "sortPosition" && this.checked) {
            document.getElementById("sortPrice").checked = false;
            sortRowsByPosition();
        }
    }

    document.getElementById("sortPrice").addEventListener("change", handleSortChange);
    document.getElementById("sortPosition").addEventListener("change", handleSortChange);

    // Domyślne sortowanie po cenie
    sortRowsByPrice();
});
