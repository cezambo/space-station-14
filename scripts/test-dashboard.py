#!/usr/bin/env python3
"""Live progress for scripts/run-integration-shards.sh.

  scripts/test-dashboard.py            serve http://127.0.0.1:6081 (auto-refreshing page)
  scripts/test-dashboard.py --once     print a text summary and exit

Reads docs/reports/raw/integration-shards/{events.jsonl,<shard>.log}. Expected test counts come from
the last full-run TRX (docs/reports/raw/integration.trx); shard durations are learned into history.json.
"""
import http.server
import json
import re
import sys
import time
import xml.etree.ElementTree as ET
from datetime import datetime
from zoneinfo import ZoneInfo
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
OUT = ROOT / "docs/reports/raw/integration-shards"
FULL_TRX = ROOT / "docs/reports/raw/integration.trx"
HISTORY = OUT / "history.json"
NS = "Content.IntegrationTests.Tests."
ORDER = ["entitytest", "gamerules", "rest-ag", "rest-hz"]
LABELS = {"entitytest": "EntityTest (spawn de todas as entidades)", "gamerules": "GameRules (modos de jogo)",
          "rest-ag": "Demais testes A–G (atmos, construção, dano, ...)",
          "rest-hz": "Demais testes H–Z (protótipos, lobby, power, ...)"}
RESULT_RE = re.compile(r"^\s+(Passed|Failed|Skipped) (.+?) \[")
TIMEOUT_S = 20 * 60


def shard_of(fqn):
    if fqn.startswith(NS + "EntityTest."):
        return "entitytest"
    if fqn.startswith(NS + "GameRules."):
        return "gamerules"
    first = fqn[len(NS):len(NS) + 1] if fqn.startswith(NS) else "Z"
    return "rest-ag" if "A" <= first <= "G" else "rest-hz"


_expected = None


def expected_counts():
    global _expected
    if _expected is None:
        _expected = {s: 0 for s in ORDER}
        if FULL_TRX.exists():
            tns = "{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}"
            root = ET.parse(FULL_TRX).getroot()
            classes = {u.get("id"): u.find(tns + "TestMethod").get("className") for u in root.iter(tns + "UnitTest")}
            for r in root.iter(tns + "UnitTestResult"):
                cls = classes.get(r.get("testId"), "")
                _expected[shard_of(cls + "." + r.get("testName", ""))] += 1
    return _expected


def load_history():
    try:
        return json.loads(HISTORY.read_text())
    except Exception:
        return {}


def read_events():
    p = OUT / "events.jsonl"
    if not p.exists():
        return []
    evs = []
    for line in p.read_text().splitlines():
        try:
            evs.append(json.loads(line))
        except Exception:
            pass
    return evs


def fmt(secs):
    if secs is None:
        return "?"
    secs = int(max(secs, 0))
    return f"{secs // 60}m{secs % 60:02d}s" if secs >= 60 else f"{secs}s"


def status():
    now = time.time()
    evs = read_events()
    exp = expected_counts()
    hist = load_history()
    planned = next((e["extra"].split(",") for e in evs if e["event"] == "run_start"), ORDER)
    starts = {e["shard"]: e["t"] for e in evs if e["event"] == "shard_start"}
    ends = {e["shard"]: e["t"] for e in evs if e["event"] == "shard_end"}
    timeouts = {e["shard"] for e in evs if e["event"] == "shard_timeout"}
    run_start = next((e["t"] for e in evs if e["event"] == "run_start"), None)
    run_end = next((e["t"] for e in evs if e["event"] == "run_end"), None)
    building = any(e["event"] == "build_start" for e in evs) and not any(e["event"] in ("build_end", "build_failed") for e in evs)

    shards, remaining_total, eta_known = [], 0.0, True
    for s in planned:
        log = OUT / f"{s}.log"
        passed = failed = skipped = 0
        failures, last = [], None
        if s in starts and log.exists():
            for line in log.read_text(errors="ignore").splitlines():
                m = RESULT_RE.match(line)
                if m:
                    kind, name = m.groups()
                    last = name
                    if kind == "Passed":
                        passed += 1
                    elif kind == "Failed":
                        failed += 1
                        failures.append(name)
                    else:
                        skipped += 1
        done = passed + failed + skipped
        state = "concluído" if s in ends else "rodando" if s in starts else "aguardando"
        total = done if state == "concluído" else max(exp.get(s, 0), done)
        elapsed = (ends.get(s, now) - starts[s]) if s in starts else None
        if state == "concluído":
            remaining = 0
            if s not in timeouts:
                hist[s] = ends[s] - starts[s]
        elif state == "rodando":
            if done >= 5 and total:
                remaining = elapsed / done * (total - done)
            elif s in hist:
                remaining = max(hist[s] - elapsed, 0)
            else:
                remaining = None
        else:
            remaining = hist.get(s)
        if remaining is None:
            eta_known = False
        else:
            remaining_total += remaining
        shards.append(dict(name=s, label=LABELS.get(s, s), state=state, passed=passed, failed=failed,
                           skipped=skipped, done=done, total=total, elapsed=elapsed, remaining=remaining,
                           last=last, failures=failures[-10:], timeout=s in timeouts,
                           pool_limit_left=(TIMEOUT_S - elapsed) if state == "rodando" and elapsed else None))
    try:
        HISTORY.write_text(json.dumps(hist, indent=1))
    except Exception:
        pass
    return dict(now=now, building=building, finished=run_end is not None, started=run_start is not None,
                elapsed=(run_end or now) - run_start if run_start else None,
                remaining=remaining_total if eta_known else None, remaining_partial=remaining_total,
                shards=shards)


def text():
    st = status()
    lines = []
    head = "concluído" if st["finished"] else "compilando" if st["building"] else "rodando" if st["started"] else "não iniciado"
    eta = fmt(st["remaining"]) if st["remaining"] is not None else f">= {fmt(st['remaining_partial'])}"
    lines.append(f"Integration tests: {head} | decorrido {fmt(st['elapsed'])} | restante {eta}")
    for s in st["shards"]:
        pct = f"{100 * s['done'] / s['total']:.0f}%" if s["total"] else "-"
        lines.append(f"  [{s['state']:10}] {s['name']:10} {s['done']}/{s['total']} ({pct})  ok={s['passed']} falha={s['failed']} "
                     f"skip={s['skipped']}  decorrido={fmt(s['elapsed'])} restante={fmt(s['remaining'])}"
                     + ("  TIMEOUT 20min!" if s["timeout"] else ""))
        if s["state"] == "rodando" and s["last"]:
            lines.append(f"      último: {s['last']}")
        for f in s["failures"]:
            lines.append(f"      FALHOU: {f}")
    return "\n".join(lines)


PAGE = """<!doctype html><html lang="pt-br"><head><meta charset="utf-8"><title>SS14 testes</title>
<style>
body{font:14px system-ui,sans-serif;background:#111418;color:#dde3ea;margin:24px;max-width:900px}
h1{font-size:20px;margin:0 0 4px} .sub{color:#8a96a3;margin-bottom:20px}
.card{background:#1a1f26;border:1px solid #2a323c;border-radius:8px;padding:14px 16px;margin-bottom:12px}
.row{display:flex;justify-content:space-between;align-items:baseline;gap:12px}
.name{font-weight:600} .state{font-size:12px;padding:2px 8px;border-radius:10px;background:#2a323c}
.rodando{background:#1f4a7a} .concluído{background:#1f5a36} .aguardando{background:#333}
.bar{height:10px;background:#2a323c;border-radius:5px;overflow:hidden;margin:10px 0 8px;display:flex}
.ok{background:#3fb96b} .bad{background:#e5534b} .skip{background:#8a96a3}
.meta{color:#8a96a3;font-size:13px} .fail{color:#e5534b;font-size:13px;margin-top:6px;word-break:break-all}
.warn{color:#e5a34b;font-size:13px;margin-top:6px} .big{font-size:26px;font-weight:600}
</style></head><body>
<h1>Integration tests: progresso</h1><div class="sub" id="sub">carregando...</div>
<div id="cards"></div>
<script>
function fmt(s){if(s===null||s===undefined)return "?";s=Math.max(0,Math.round(s));return s>=60?Math.floor(s/60)+"m"+String(s%60).padStart(2,"0")+"s":s+"s"}
function esc(t){return String(t).replace(/[&<>]/g,c=>({"&":"&amp;","<":"&lt;",">":"&gt;"}[c]))}
async function tick(){
  try{
    const st=await (await fetch("status.json",{cache:"no-store"})).json();
    const head=st.finished?"concluído":st.building?"compilando":st.started?"rodando":"não iniciado";
    const eta=st.remaining!==null?fmt(st.remaining):"pelo menos "+fmt(st.remaining_partial);
    document.getElementById("sub").innerHTML=`<span class="big">${head}</span> &nbsp; decorrido ${fmt(st.elapsed)} &nbsp;·&nbsp; restante estimado <b>${eta}</b>`;
    document.getElementById("cards").innerHTML=st.shards.map(s=>{
      const t=s.total||1, w=x=>(100*x/t).toFixed(2)+"%";
      const pct=s.total?Math.round(100*s.done/s.total)+"%":"-";
      let extra="";
      if(s.state==="rodando"&&s.last) extra+=`<div class="meta">último teste: ${esc(s.last)}</div>`;
      if(s.state==="rodando"&&s.pool_limit_left!==null&&s.pool_limit_left<300) extra+=`<div class="warn">faltam ${fmt(s.pool_limit_left)} para o limite de 20 min do upstream</div>`;
      if(s.timeout) extra+=`<div class="warn">este lote bateu o limite de 20 min; precisa ser dividido</div>`;
      extra+=s.failures.map(f=>`<div class="fail">falhou: ${esc(f)}</div>`).join("");
      return `<div class="card"><div class="row"><span class="name">${esc(s.label)}</span><span class="state ${s.state}">${s.state}</span></div>
        <div class="bar"><div class="ok" style="width:${w(s.passed)}"></div><div class="bad" style="width:${w(s.failed)}"></div><div class="skip" style="width:${w(s.skipped)}"></div></div>
        <div class="row meta"><span>${s.done} / ${s.total} (${pct}) · ${s.passed} ok · ${s.failed} falhas · ${s.skipped} pulados</span>
        <span>decorrido ${fmt(s.elapsed)} · restante ${fmt(s.remaining)}</span></div>${extra}</div>`}).join("");
  }catch(e){document.getElementById("sub").textContent="sem dados ainda ("+e+")"}
}
tick();setInterval(tick,3000);
</script></body></html>"""


class Handler(http.server.BaseHTTPRequestHandler):
    def do_GET(self):
        if self.path.startswith("/status.json"):
            body, ctype = json.dumps(status()).encode(), "application/json"
        elif self.path in ("/", "/index.html"):
            body, ctype = PAGE.encode(), "text/html; charset=utf-8"
        else:
            self.send_error(404)
            return
        self.send_response(200)
        self.send_header("Content-Type", ctype)
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def log_message(self, *args):
        pass


def bar(s, width=30):
    if not s["total"]:
        return "░" * width
    ok = round(width * s["passed"] / s["total"])
    bad = round(width * s["failed"] / s["total"])
    skip = round(width * s["skipped"] / s["total"])
    return "█" * ok + "▓" * bad + "▒" * skip + "░" * max(width - ok - bad - skip, 0)


def markdown():
    st = status()
    now = datetime.fromtimestamp(st["now"], ZoneInfo("America/Sao_Paulo")).strftime("%H:%M:%S")
    head = ("✅ concluído" if st["finished"] and not any(s["failed"] for s in st["shards"])
            else "❌ concluído com falhas" if st["finished"]
            else "🔨 compilando" if st["building"] else "▶️ rodando" if st["started"] else "⏸ não iniciado")
    eta = fmt(st["remaining"]) if st["remaining"] is not None else f"pelo menos {fmt(st['remaining_partial'])}"
    out = [f"# Progresso dos testes de integração",
           "",
           f"**Estado:** {head}  ·  **Decorrido:** {fmt(st['elapsed'])}  ·  **Restante (estimativa):** {eta if not st['finished'] else '0s'}",
           "",
           (f"_Execução terminada; última atualização às {now}._" if st["finished"]
            else f"_Atualizado às {now}. Este arquivo se atualiza sozinho a cada 5 s enquanto os testes rodam._"),
           "",
           "| Lote | Estado | Progresso | Feitos | OK | Falhas | Pulados | Decorrido | Restante |",
           "|---|---|---|---:|---:|---:|---:|---:|---:|"]
    for s in st["shards"]:
        pct = f"{100 * s['done'] / s['total']:.0f}%" if s["total"] else "-"
        out.append(f"| {s['label']} | {s['state']} | `{bar(s)}` {pct} | {s['done']}/{s['total']} | {s['passed']} | "
                   f"{s['failed']} | {s['skipped']} | {fmt(s['elapsed'])} | {fmt(s['remaining']) if s['state'] != 'concluído' else '-'} |")
    out.append("")
    out.append("Legenda da barra: `█` ok · `▓` falha · `▒` pulado · `░` ainda não rodou")
    for s in st["shards"]:
        if s["state"] == "rodando" and s["last"]:
            out += ["", f"**Rodando agora ({s['name']}):** último teste terminado: `{s['last']}`"]
            if s["pool_limit_left"] is not None and s["pool_limit_left"] < 300:
                out.append(f"⚠️ faltam {fmt(s['pool_limit_left'])} para o limite de 20 min do upstream")
        if s["timeout"]:
            out += ["", f"⚠️ **{s['name']}** bateu o limite de 20 min do upstream; o lote precisa ser dividido."]
        if s["failures"]:
            out += ["", f"**Falhas em {s['name']}:**"] + [f"- `{f}`" for f in s["failures"]]
    return "\n".join(out) + "\n"


def watch(path, interval=5.0):
    path = Path(path)
    while True:
        tmp = path.with_suffix(".tmp")
        tmp.write_text(markdown(), encoding="utf-8")
        tmp.replace(path)
        if status()["finished"]:
            return
        time.sleep(interval)


if __name__ == "__main__":
    if "--once" in sys.argv:
        print(text())
        sys.exit(0)
    if "--watch" in sys.argv:
        watch(sys.argv[sys.argv.index("--watch") + 1])
        sys.exit(0)
    port = 6081
    print(f"dashboard: http://127.0.0.1:{port}/")
    http.server.ThreadingHTTPServer(("127.0.0.1", port), Handler).serve_forever()
