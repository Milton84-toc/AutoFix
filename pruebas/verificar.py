#!/usr/bin/env python3
"""Pruebas reproducibles de AutoFix contra la API en modo Development."""
import argparse
import base64
from concurrent.futures import ThreadPoolExecutor
from datetime import datetime, timedelta, timezone
import json
from pathlib import Path
import sqlite3
from threading import Barrier
from urllib.error import HTTPError
from urllib.request import Request, urlopen
from uuid import uuid4

PNG = base64.b64decode(
    "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADElEQVQIHWP4z8DwHwAFgAI/"
    "WdbvJwAAAABJRU5ErkJggg=="
)
PASSWORD = "AutoFixDemo!10"


def ejecutar(args):
    notas = []
    uid = uuid4().hex[:9]
    base = args.url.rstrip("/")

    def llamar(metodo, ruta, token=None, datos=None, contenido=None):
        headers = {"X-Correlation-ID": str(uuid4())}
        if token:
            headers["Authorization"] = f"Bearer {token}"
        if contenido:
            headers["Content-Type"], cuerpo = contenido
        elif datos is not None:
            headers["Content-Type"] = "application/json"
            cuerpo = json.dumps(datos).encode()
        else:
            cuerpo = None
        req = Request(base + "/api/" + ruta, data=cuerpo, headers=headers, method=metodo)
        try:
            respuesta = urlopen(req, timeout=40)
        except HTTPError as error:
            respuesta = error
        with respuesta:
            raw = respuesta.read()
            if "json" in respuesta.headers.get("Content-Type", ""):
                raw = json.loads(raw)
            return respuesta.code, raw, respuesta.headers.get("X-Correlation-ID")

    def ok(metodo, ruta, token=None, datos=None, esperado=200, contenido=None):
        code, respuesta, cid = llamar(metodo, ruta, token, datos, contenido)
        assert code == esperado, f"{metodo} {ruta}: esperaba {esperado}, recibí {code}: {str(respuesta)[:350]}"
        assert cid, f"Falta Correlation ID en {ruta}"
        return respuesta, cid

    def pasar(numero, texto):
        linea = f"{numero}. PASS · {texto}"
        notas.append(linea)
        print(linea, flush=True)

    def entrar(nombre):
        return ok("POST", "auth/login", datos={
            "email": f"{nombre}@autofix.test", "password": PASSWORD
        })[0]["token"]

    def subir(ruta, token, nombre, datos, tipo=None, esperado=201):
        borde = "autofix" + uuid4().hex
        cuerpo = b""
        if tipo is not None:
            cuerpo = (f'--{borde}\r\nContent-Disposition: form-data; name="tipo"\r\n\r\n{tipo}\r\n').encode()
        cuerpo += (f'--{borde}\r\nContent-Disposition: form-data; name="archivo"; filename="{nombre}"'
                   '\r\nContent-Type: image/png\r\n\r\n').encode() + datos + f'\r\n--{borde}--\r\n'.encode()
        return ok("POST", ruta, token, esperado=esperado,
                  contenido=(f"multipart/form-data; boundary={borde}", cuerpo))[0]

    admin, recep, superv, mec, cliente_demo = map(entrar, ("admin", "recepcion", "supervisor", "mecanico", "cliente"))
    nombre = "Cliente de prueba AutoFix"
    correo = f"{uid}@autofix.test"
    perfil = {"nombre": nombre, "dui": uid, "telefono": "7000-0001", "email": correo, "direccion": "Santa Ana"}
    cliente_id = ok("POST", "clientes", recep, perfil, 201)[0]["id"]
    ok("POST", f"clientes/{cliente_id}/activar", recep, {"password": PASSWORD}, 204)
    cliente = ok("POST", "auth/login", datos={"email": correo, "password": PASSWORD})[0]["token"]
    otro = ok("POST", "auth/registro", datos={
        **perfil, "dui": "alt" + uid, "email": "alt" + correo, "password": PASSWORD, "rol": "Administrador"
    }, esperado=201)[0]
    assert otro["rol"] == "Cliente" and not otro["permisos"]
    assert ok("GET", "vehiculos", otro["token"])[0] == []
    ids = []
    for n in range(2):
        datos = {"clienteId": cliente_id, "placa": f"AF{uid}{n}", "marca": "Nissan",
                 "modelo": "Sentra" if n == 0 else "Versa", "anio": 2018, "color": "Gris", "chasis": f"{uid}{n}"}
        ids.append(ok("POST", "vehiculos", recep, datos, 201)[0]["id"])
    visibles = ok("GET", "vehiculos", cliente)[0]
    assert {v["id"] for v in visibles} == set(ids)
    pasar("01", f"Cliente #{cliente_id} y dos vehículos guardados y visibles en su cuenta.")

    ahora = datetime.now(timezone.utc)
    inicio = ahora + timedelta(days=30, seconds=int(uid[:4], 16))
    fin = inicio + timedelta(minutes=30)
    ordenes = []

    def nueva(n, sufijo=""):
        d = {"vehiculoId": ids[n], "servicio": "Servicio de frenos " + uid + sufijo,
             "problema": "Ruido al frenar.", "finEstimado": (fin + timedelta(days=1)).isoformat()}
        return ok("POST", "ordenes", recep, d, 201)[0]

    a = nueva(0)
    b = nueva(1)
    ordenes.extend([a["id"], b["id"]])
    assert a["estado"] == 0
    ok("POST", "ordenes", recep, {"vehiculoId": ids[0], "servicio": f"  SERVICIO DE   FRENOS {uid.upper()}  ",
       "problema": "Duplicada", "finEstimado": fin.isoformat()}, 409)
    pasar("02", f"Orden {a['numero']} en Recibido. Duplicada rechazada con HTTP 409.")

    mecanicos = ok("GET", "mecanicos", superv)[0]
    mecanico = next(m for m in mecanicos if m["email"] == "mecanico@autofix.test")["id"]
    def detalle(oid, token=admin):
        return ok("GET", f"ordenes/{oid}", token)[0]
    def cambiar(oid, accion, token, datos=None, metodo="POST", esperado=204):
        return ok(metodo, f"ordenes/{oid}/{accion}", token,
                  {"version": detalle(oid)["version"], **(datos or {})}, esperado)
    def turno(desde, hasta):
        return {"mecanicoId": mecanico, "inicio": desde.isoformat(), "fin": hasta.isoformat()}

    cambiar(a["id"], "asignar", superv, turno(inicio, fin))
    assert detalle(a["id"], mec)["mecanicoId"] == mecanico
    cambiar(b["id"], "asignar", superv, turno(fin, fin + timedelta(minutes=30)))
    pasar("03", f"Asignación a mecánico #{mecanico}; dos horarios consecutivos aceptados.")
    _, rechazo_cid = cambiar(a["id"], "estado", cliente, {"estado": 1}, esperado=403)
    ok("GET", f"ordenes/{a['id']}", cliente_demo, esperado=404)
    pasar("04", f"Cliente sin permiso recibió HTTP 403. Correlation ID {rechazo_cid}.")

    parte = ok("POST", "repuestos", admin, {
        "codigo": "AF-" + uid, "nombre": "Pastillas de prueba", "descripcion": "Juego de laboratorio",
        "categoria": "Frenos", "marca": "AutoFix", "precio": 24.5, "stockMinimo": 2, "existencias": 2,
    }, 201)[0]["id"]
    otra_parte = ok("POST", "repuestos", admin, {
        "codigo": "FLT-" + uid, "nombre": "Filtro de prueba", "descripcion": "Filtro de laboratorio",
        "categoria": "Filtros", "marca": "AutoFix", "precio": 8, "stockMinimo": 1, "existencias": 2,
    }, 201)[0]["id"]
    for oid in ordenes:
        cambiar(oid, "estado", mec, {"estado": 1})
        cambiar(oid, "diagnostico", mec, {"diagnostico": "Pastillas con desgaste."}, metodo="PUT")
        cambiar(oid, "presupuesto", mec, {"manoObra": 15.5, "observaciones": "Revisión completa",
                                         "lineas": [{"repuestoId": parte, "cantidad": 2}]})
        p = detalle(oid)["presupuesto"]
        assert p["total"] == 64.5 and p["lineas"][0]["subtotal"] == 49
    pasar("05", "Presupuesto de $64.50 con $15.50 de mano de obra y subtotal de $49.00 en repuestos.")

    for oid in ordenes:
        cambiar(oid, "estado", mec, {"estado": 3})
        cambiar(oid, "aprobar", recep, esperado=403)
        cambiar(oid, "aprobar", superv)
        assert detalle(oid, cliente)["orden"]["estado"] == 4
        assert detalle(oid, cliente)["presupuesto"]["aprobadoPor"] == "supervisor@autofix.test"
    pasar("06", "Supervisor aprobó el presupuesto y el cliente vio el estado Aprobado.")

    ruta_foto = f"ordenes/{a['id']}/evidencias"
    subir(ruta_foto, mec, "daño.txt", PNG, 1, 400)
    subir(ruta_foto, mec, "daño.png", b"1234567890", 1, 400)
    subir(ruta_foto, mec, "grande.png", PNG + b"0" * (5 * 1024 * 1024), 1, 400)
    foto = subir(ruta_foto, mec, "daño.png", PNG, 1)["foto"]
    assert detalle(a["id"], cliente)["evidencias"][0]["foto"] == foto
    assert ok("GET", "imagenes/" + foto, cliente)[0] == PNG
    ok("GET", "imagenes/" + foto, cliente_demo, esperado=404)
    subir(f"vehiculos/{ids[0]}/foto", recep, "vehiculo.png", PNG)
    subir(f"repuestos/{parte}/foto", admin, "repuesto.png", PNG)
    if args.db:
        with sqlite3.connect(args.db) as db:
            assert db.execute("SELECT Foto FROM Evidencia WHERE OrdenId = ?", (a["id"],)).fetchone()[0] == foto
    pasar("07", "Foto de daño guardada y recuperada por propietario; formato, tamaño y acceso ajeno rechazados.")

    def stock(repuesto):
        return next(r["existencias"] for r in ok("GET", "repuestos", admin)[0] if r["id"] == repuesto)

    def simultaneas(trabajos):
        barrera = Barrier(2)
        def enviar(t):
            barrera.wait(timeout=10)
            return llamar(*t)
        with ThreadPoolExecutor(max_workers=2) as pool:
            return list(pool.map(enviar, trabajos))

    assert stock(parte) == 2
    resp = simultaneas([("POST", f"ordenes/{oid}/estado", mec, {"version": detalle(oid)["version"], "estado": 5})
                        for oid in ordenes])
    assert sorted(r[0] for r in resp) == [204, 409], resp
    assert stock(parte) == 0
    ganador = ordenes[next(i for i, r in enumerate(resp) if r[0] == 204)]
    perdedor = next(oid for oid in ordenes if oid != ganador)
    movimientos = ok("GET", "movimientos", admin)[0]
    assert [(m["cantidad"], m["ordenId"]) for m in movimientos if m["repuestoId"] == parte and m["cantidad"] < 0] == [(-2, ganador)]
    pasar("08", "Al iniciar la reparación se descontaron 2 unidades y quedó una salida vinculada a la orden.")

    cambiar(perdedor, "estado", mec, {"estado": 5}, esperado=409)
    ok("POST", "movimientos", admin, {"repuestoId": parte, "entrada": False, "cantidad": 1, "motivo": "Prueba"}, 409)
    assert stock(parte) == 0 and detalle(perdedor)["orden"]["estado"] == 4
    # Una salida previa de la misma transacción debe revertirse si falla el siguiente repuesto.
    extra = nueva(0, " múltiple")["id"]
    cambiar(extra, "asignar", superv, turno(fin + timedelta(hours=1), fin + timedelta(hours=2)))
    cambiar(extra, "estado", mec, {"estado": 1})
    cambiar(extra, "diagnostico", mec, {"diagnostico": "Prueba de reversión."}, metodo="PUT")
    cambiar(extra, "presupuesto", mec, {"manoObra": 0, "lineas": [
        {"repuestoId": otra_parte, "cantidad": 1}, {"repuestoId": parte, "cantidad": 1}]})
    cambiar(extra, "estado", mec, {"estado": 3})
    cambiar(extra, "aprobar", superv)
    cambiar(extra, "estado", mec, {"estado": 5}, esperado=409)
    assert stock(otra_parte) == 2 and detalle(extra)["orden"]["estado"] == 4
    pasar("09", "Existencias insuficientes devolvieron HTTP 409; se revirtió el descuento parcial.")

    c, d = nueva(0, " horario"), nueva(1, " horario")
    conflictos = [c["id"], d["id"]]
    mismo = turno(fin + timedelta(hours=3), fin + timedelta(hours=4))
    resp_horario = simultaneas([("POST", f"ordenes/{oid}/asignar", superv, {
        "version": detalle(oid)["version"], **mismo}) for oid in conflictos])
    assert sorted(r[0] for r in resp_horario) == [204, 409], resp_horario
    assert sum(detalle(oid)["mecanicoId"] == mecanico for oid in conflictos) == 1
    version_previa = detalle(ganador)["version"]
    subir(f"ordenes/{ganador}/evidencias", mec, "final.png", PNG, 3)
    error_version, _ = ok("POST", f"ordenes/{ganador}/finalizar", superv, {"version": version_previa}, 409)
    assert "Otro usuario" in error_version["mensaje"]
    cambiar(ganador, "finalizar", superv)
    cambiar(ganador, "estado", recep, {"estado": 7})
    assert detalle(ganador)["orden"]["estado"] == 7
    pasar("10A", f"Inventario concurrente: {[r[0] for r in resp]}; saldo 0 y una sola salida.")
    pasar("10B", f"Horario concurrente: {[r[0] for r in resp_horario]}; un solo turno. Versión obsoleta: 409.")
    ok("GET", "ordenes", esperado=401)
    pasar("11", "Una petición sin JWT devolvió HTTP 401.")

    if args.log:
        lineas = Path(args.log).read_text(encoding="utf-8", errors="replace").splitlines()
        trazas = [s.strip() for s in lineas if rechazo_cid in s and "HTTP 403" in s]
        assert len(trazas) == 1 and all(c in trazas[0] for c in (correo, "DuracionMs", "/estado", "Usuario=")), trazas
        pasar("12", "El log de la operación rechazada coincide con el usuario, la ruta, el HTTP 403 y el Correlation ID.")
        notas.append(trazas[0])
    else:
        notas.append("12. Pendiente de inspección del log. Pasa --log para validarlo automáticamente.")
    if args.salida:
        Path(args.salida).write_text("\n".join(notas) + "\n", encoding="utf-8")
    if args.datos:
        Path(args.datos).write_text(json.dumps({"a": a["id"], "ganador": ganador, "cliente": correo,
                                             "repuesto": parte, "foto": foto}), encoding="utf-8")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--url", default="http://localhost:5080")
    parser.add_argument("--log", help="Ruta al log de la API")
    parser.add_argument("--db", help="Ruta a la base SQLite")
    parser.add_argument("--salida", help="Ruta para guardar los resultados")
    parser.add_argument("--datos", help="Ruta JSON para identificar los datos de la ejecución")
    ejecutar(parser.parse_args())
