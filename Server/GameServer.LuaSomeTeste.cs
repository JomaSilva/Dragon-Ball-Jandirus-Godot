using Godot;
using Jandirus.Core.Forms;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// A BANCADA DA LUA QUE SOME (`--luasometeste`).
///
/// Os pedidos do dono (2026-09-06) que moram no SERVIDOR:
///   * *"a aba de formas nao ta mostrando a forma atual que eu to"* -- a ficha de atributos dizia
///     `base` com o corpo em Oozaru; agora diz a fera;
///   * *"a forma do oozaru e desfeita no momento que a fonte que deu o poder pra virar (lua etc) sumir,
///     entao quando amanhecer e a lua sair do ceu, todos que estao em oozaru voltam a forma base"*.
///
/// E o que se achou no caminho: o relogio da fera (`TickDoOozaru`) estava FORA do laco de producao desde
/// a55b464 -- so as bancadas o chamavam, direto. A primeira familia mede o laco de verdade
/// (`TickDosRelogiosDoCorpo`), e nao a funcao.
///
/// Roda no primeiro login, como a `--luaferateste`, porque precisa dos moldes (nasce Saiyajin pelo
/// caminho de producao) e do ceu; adianta o relogio do mundo pra achar a lua e devolve no `finally`.
/// </summary>
public sealed partial class GameServer
{
	private bool _luaSomeDeTeste;

	private void RodarBancadaDaLuaQueSome(ServerPlayer pl)
	{
		GD.Print("\n[luasome] ================ A LUA SE FOI, E COM ELA A BESTA (pedido do dono, 2026-09-06) ================");
		int ok = 0, falhou = 0;
		void Checa(string nome, bool cond, string detalhe = "")
		{
			if (cond) { ok++; GD.Print($"[luasome]   OK    {nome}" + (detalhe.Length > 0 ? $"   [{detalhe}]" : "")); }
			else { falhou++; GD.PrintErr($"[luasome]   FALHA {nome}   [{detalhe}]"); }
		}
		List<string> Ouvido()
		{
			List<string> ditos = EscutaDeAvisos ?? [];
			EscutaDeAvisos = null;
			return ditos;
		}

		double ceuGuardado = _adiantoDoCeu;
		var forjados = new List<ServerPlayer>();
		try
		{
			var palco = ZoneKey.Premade("Earth");
			ZoneCollision? mapa = MapaDaZonaOuCatalogo(palco);
			if (mapa == null || _moldes == null || _racas == null)
			{
				Checa("PRECONDICAO: a Earth tem mapa e os moldes/racas carregaram", false);
				return;
			}

			// A LUA CHEIA NO ALTO -- o mesmo ceu que a bancada da fera usa.
			AjustarCeuDaTerra(hora: 0.90, fase: Ceu.Cheia);
			EstadoDoCeu noite = Ceu.De(RelogioDaZona(palco), TempoDoMundo);
			Checa("PRECONDICAO: lua cheia no alto do ceu da Terra", noite.Cheia && noite.LuaNoCeu,
				  $"fase {noite.Fase}, altura {noite.Altura:0.00}");

			// =====================================================================
			// 1) O RELOGIO DA FERA RODA NO LACO DE PRODUCAO
			// =====================================================================
			GD.Print("[luasome] -- 1) o relogio da fera roda no laco de producao --");
			ServerPlayer? a = ForjarSaiyajin(palco, mapa, forjados);
			if (a == null) { Checa("PRECONDICAO: nasceu um Saiyajin com rabo", false); return; }
			Apeshit(a);
			Checa("um Saiyajin com rabo sob a lua cheia vira a fera", a.Oozaru != FormaOozaru.Nao, a.Oozaru.ToString());
			TickDosRelogiosDoCorpo(Protocol.TickSeconds);
			Checa("contra-exemplo: com prazo e lua, o laco de producao NAO derruba a fera", a.Oozaru != FormaOozaru.Nao, a.Oozaru.ToString());
			a.OozaruAte = NowMs() - 1;
			TickDosRelogiosDoCorpo(Protocol.TickSeconds);
			Checa("vencido o prazo, o LACO DE PRODUCAO (e nao o `TickDoOozaru` chamado a mao) derruba a fera "
				  + "-- o relogio estava fora do laco desde a55b464",
				  a.Oozaru == FormaOozaru.Nao, a.Oozaru.ToString());

			// =====================================================================
			// 2) A LUA SE FOI: amanheceu
			// =====================================================================
			GD.Print("[luasome] -- 2) amanheceu: a lua saiu do ceu --");
			ServerPlayer? b = ForjarSaiyajin(palco, mapa, forjados);
			if (b == null) { Checa("PRECONDICAO: nasceu o segundo Saiyajin", false); return; }
			Apeshit(b);
			Checa("PRECONDICAO: o segundo virou a fera", b.Oozaru != FormaOozaru.Nao, b.Oozaru.ToString());
			EscutaDeAvisos = [];
			TickDoOozaru(b, Protocol.TickSeconds);
			Ouvido();
			Checa("contra-exemplo: com a lua cheia no alto, o tique nao mexe na fera", b.Oozaru != FormaOozaru.Nao, b.Oozaru.ToString());

			AjustarCeuDaTerra(hora: 0.5);   // meio-dia
			EstadoDoCeu dia = Ceu.De(RelogioDaZona(palco), TempoDoMundo);
			Checa("PRECONDICAO: ao meio-dia a lua nao esta no ceu", !dia.LuaNoCeu, $"altura {dia.Altura:0.00}");
			EscutaDeAvisos = [];
			TickDoOozaru(b, Protocol.TickSeconds);
			List<string> aoAmanhecer = Ouvido();
			Checa("AMANHECEU: a fera cai no tique seguinte", b.Oozaru == FormaOozaru.Nao, b.Oozaru.ToString());
			Checa("...e o aviso diz que foi a lua", aoAmanhecer.Any(t => t.Contains("lua", StringComparison.OrdinalIgnoreCase)),
				  string.Join(" | ", aoAmanhecer));
			Checa("...e o corpo volta a ter cerebro (nao vira estatua)", b.Cerebro != null);
			Checa("...com o poder da fera devolvido (giantFormbuff = 1)", Math.Abs(b.Ficha.giantFormbuff - 1) < 1e-9, $"{b.Ficha.giantFormbuff}");

			// =====================================================================
			// 3) A LUA MINGUOU COM O CORPO AINDA NO CEU: fase que nao e cheia tambem e "a fonte sumiu"
			// =====================================================================
			GD.Print("[luasome] -- 3) a lua minguou --");
			AjustarCeuDaTerra(hora: 0.90, fase: Ceu.Cheia);
			ServerPlayer? c = ForjarSaiyajin(palco, mapa, forjados);
			if (c == null) { Checa("PRECONDICAO: nasceu o terceiro Saiyajin", false); return; }
			Apeshit(c);
			Checa("PRECONDICAO: o terceiro virou a fera", c.Oozaru != FormaOozaru.Nao, c.Oozaru.ToString());
			AjustarCeuDaTerra(hora: 0.90, fase: (int)FaseDaLua.QuartoCrescente);
			EstadoDoCeu minguante = Ceu.De(RelogioDaZona(palco), TempoDoMundo);
			Checa("PRECONDICAO: a lua esta no alto mas nao e cheia", minguante.LuaNoCeu && !minguante.Cheia, $"fase {minguante.Fase}");
			TickDoOozaru(c, Protocol.TickSeconds);
			Checa("a lua que deixou de ser cheia tambem desfaz a fera", c.Oozaru == FormaOozaru.Nao, c.Oozaru.ToString());

			// =====================================================================
			// 4) TODOS VOLTAM: dois feras, um tique do laco, nenhum macaco
			// =====================================================================
			GD.Print("[luasome] -- 4) todos voltam no mesmo amanhecer --");
			AjustarCeuDaTerra(hora: 0.90, fase: Ceu.Cheia);
			ServerPlayer? d = ForjarSaiyajin(palco, mapa, forjados);
			ServerPlayer? e = ForjarSaiyajin(palco, mapa, forjados);
			if (d == null || e == null) { Checa("PRECONDICAO: nasceram dois Saiyajins", false); return; }
			Apeshit(d); Apeshit(e);
			Checa("PRECONDICAO: os dois viraram a fera", d.Oozaru != FormaOozaru.Nao && e.Oozaru != FormaOozaru.Nao);
			AjustarCeuDaTerra(hora: 0.5);
			TickDosRelogiosDoCorpo(Protocol.TickSeconds);
			Checa("um unico tique do laco ao amanhecer devolve TODOS a forma base",
				  d.Oozaru == FormaOozaru.Nao && e.Oozaru == FormaOozaru.Nao, $"{d.Oozaru}, {e.Oozaru}");

			// =====================================================================
			// 5) A ABA FORMAS: a ficha de atributos diz a fera enquanto ela dura
			// =====================================================================
			GD.Print("[luasome] -- 5) a ficha de atributos diz a forma de agora --");
			AjustarCeuDaTerra(hora: 0.90, fase: Ceu.Cheia);
			ServerPlayer? f = ForjarSaiyajin(palco, mapa, forjados);
			if (f == null) { Checa("PRECONDICAO: nasceu o Saiyajin da ficha", false); return; }
			Apeshit(f);
			Checa("PRECONDICAO: virou a fera", f.Oozaru != FormaOozaru.Nao, f.Oozaru.ToString());
			EscutaDeAtributos = [];
			f.SigAtributos = "";
			MandarAtributos(f);
			Protocol.AtributosState? naFera = EscutaDeAtributos.LastOrDefault();
			ushort redeDaFera = Catalogo.Rede(Oozaru.Id(f.Oozaru));
			Checa("em Oozaru, `FormaAtual` da ficha e a fera (a aba Formas la o nome dela)",
				  naFera is { } nf && nf.FormaAtual == redeDaFera, $"{naFera?.FormaAtual} (fera = {redeDaFera})");
			DesfazerOozaru(f, "bancada: fim");
			EscutaDeAtributos = [];
			f.SigAtributos = "";
			MandarAtributos(f);
			Protocol.AtributosState? naBase = EscutaDeAtributos.LastOrDefault();
			Checa("de volta ao normal, `FormaAtual` volta a base (contra-exemplo)",
				  naBase is { } nb && nb.FormaAtual == Catalogo.Rede(Catalogo.IdBase), $"{naBase?.FormaAtual}");
		}
		finally
		{
			foreach (ServerPlayer c in forjados)
				if (_players.ContainsKey(c.Id)) RemoverNpc(c);
			_adiantoDoCeu = ceuGuardado;
			EscutaDeAvisos = null;
			EscutaDeAtributos = null;
		}
		GD.Print($"[luasome] ================ {ok} OK, {falhou} FALHA(S) ================");
	}
}
