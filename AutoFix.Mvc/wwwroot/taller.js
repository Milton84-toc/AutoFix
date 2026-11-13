document.querySelectorAll("form[data-confirmar]").forEach(form => {
    form.addEventListener("submit", event => {
        if (!window.confirm(form.dataset.confirmar)) event.preventDefault();
    });
});

const presupuesto = document.querySelector("form[data-presupuesto]");
if (presupuesto) {
    const lineas = presupuesto.querySelector("[data-lineas]");
    const agregar = presupuesto.querySelector("[data-agregar]");
    const plantilla = presupuesto.querySelector("[data-plantilla]");
    const moneda = new Intl.NumberFormat("es-SV", { style: "currency", currency: "USD" });
    function actualizar() {
        let total = Number(presupuesto.elements.ManoObra.value);
        const filas = lineas.querySelectorAll("[data-linea]");
        filas.forEach((fila, indice) => {
            const repuesto = fila.querySelector("select");
            const cantidad = fila.querySelector("input");
            repuesto.name = `Lineas[${indice}].RepuestoId`;
            cantidad.name = `Lineas[${indice}].Cantidad`;
            total += Number(repuesto.selectedOptions[0]?.dataset.precio ?? 0) * Number(cantidad.value);
        });
        if (agregar) agregar.disabled = filas.length >= 50;
        presupuesto.querySelector("[data-total]").textContent = `Total estimado ${moneda.format(total)}`;
    }

    agregar?.addEventListener("click", () => {
        lineas.append(plantilla.content.cloneNode(true));
        actualizar();
        lineas.lastElementChild.querySelector("select").focus();
    });
    lineas.addEventListener("click", event => {
        const boton = event.target.closest("[data-quitar]");
        if (!boton) return;
        boton.closest("[data-linea]").remove();
        actualizar();
        agregar?.focus();
    });
    presupuesto.addEventListener("input", actualizar);
    actualizar();
}
