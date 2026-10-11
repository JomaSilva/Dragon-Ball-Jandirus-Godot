using Godot;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// A BANCADA DA SAIDA NO MEIO DO ZANZOCLASH (`--saidaembateteste`).
///
/// A regra e o que foi medido antes do conserto estao no cabecalho do `GameServer.SaidaDoEmbate.cs`. Aqui dois
/// clientes crus entram pela porta, ficam em embate (o `Comecar` de producao, com o placar que a bancada
/// escolhe) e um deles SAI DO JOGO -- a queda do par, que roda o `Drop` de producao:
///
///   1. quem sai PERDENDO: vai pro disco VISIVEL e onde estava; quem fica nao e teleportado pra cima dele, nao
///      bate em ninguem e sai do embate na hora, visivel e sem trava;
///   2. quem sai GANHANDO: nao e recolocado nas costas de quem fica e nao bate nele na saida;
///   3. o login seguinte: a ficha nao diz "oculto" e o poder expresso e o dele;
///   4. O SAVE QUE JA ESTAVA OCULTO SE CURA NO LOGIN: o save periodico cai no meio de um embate (isso continua
///      podendo acontecer), o processo morre, e quem volta acorda visivel e com o poder inteiro;
///   5. os CONTRA-EXEMPLOS: a saida de antes (`SaidaDoEmbateDeAntesDeTeste`: gravado oculto; quem fica e
///      teleportado e a cena de fecho acontece) e o login que nao cura (`LoginNaoCuraOOcultoDeTeste`: poder
///      expresso preso em 5).
///
///     Godot --headless --path . --server --port 8289 --saidaembateteste
/// </summary>
public partial class GameServer
{
	private int _saidaOk, _saidaFalhou;
	private bool _saidaEmbateRodando, _saidaEmbateJaRodou;

	/// <summary>O BP dos dois corpos da bancada: bem acima de 5, que e onde o oculto trava o poder expresso.</summary>
	private const double BpDaBancadaDaSaidaDoEmbate = 60;

	private void AfirmarSaida(string nome, bool cond, string detalhe = "")
	{
		if (cond) { _saidaOk++; GD.Print($"[saidaembate]   OK    {nome}" + (detalhe.Length > 0 ? $"   [{detalhe}]" : "")); }
		else { _saidaFalhou++; GD.PrintErr($"[saidaembate]   FALHA {nome}   [{detalhe}]"); }
	}

	public void RodarBancadaDaSaidaDoEmbate(int porta)
	{
		if (_saidaEmbateRodando || _saidaEmbateJaRodou) return;   // a subida que a propria bancada faz chega aqui de novo
		_saidaEmbateRodando = _saidaEmbateJaRodou = true;
		_saidaOk = _saidaFalhou = 0;
		GD.Print("[saidaembate] ================ SAIR DO JOGO NO MEIO DE UM ZANZOCLASH (dono, 2026-10-11) ================");

		const string contaB = "bancada_saida_sai", contaC = "bancada_saida_fica";
		const string nomeB = "Saidor", nomeC = "Ficante";
		const float T = ZoneCollision.TileSize;
		const int tilesEntreOsDois = 4;
		var b = new ClienteCruDeTeste();   // o que SAI
		var c = new ClienteCruDeTeste();   // o que FICA
		var mesa = new MesaDeClientesCrus(this, porta, b, c);
		float Tiles(Vec2 de, Vec2 ate) => Vec2.Distance(de, ate) / T;

		try
		{
			ServerPlayer? corpoB = mesa.EntraNoMundo(b, contaB, nomeB), corpoC = mesa.EntraNoMundo(c, contaC, nomeC);
			ZoneCollision? mapa = corpoC == null ? null : MapaDaZonaOuCatalogo(corpoC.Zone);
			AfirmarSaida("(montagem) os dois entram no mundo pela porta, na mesma zona", corpoB != null && corpoC != null && mapa != null && corpoB.Zone.Hash == corpoC.Zone.Hash);
			if (corpoB == null || corpoC == null || mapa == null) return;
			ZoneKey terra = corpoC.Zone;
			Vec2 ancora = PontoDeNascimento(terra);
			Vec2 lugarC = mapa.PontoLivrePerto(ancora), lugarB = mapa.PontoLivrePerto(ancora + new Vec2(tilesEntreOsDois * T, 0));
			ServerPlayer oQueFica = corpoC;
			oQueFica.Ficha.BP = BpDaBancadaDaSaidaDoEmbate;

			// O QUE UMA SAIDA DEIXOU: o disco de quem saiu, quem ficou, e o corpo de quem saiu depois do save.
			(bool Saiu, CharacterSave? Disco, float FicouAndou, bool FicouInteiro, float SaiuAndou, bool SaiuInteiro) Cena(bool quemSaiVence)
			{
				ServerPlayer? oQueSai = mesa.CorpoDe(contaB) ?? mesa.EntraNoMundo(b, contaB, nomeB);
				if (oQueSai == null) return default;
				oQueSai.Ficha.BP = BpDaBancadaDaSaidaDoEmbate;
				if (oQueSai.Zone.Hash != terra.Hash) MoveToZone(oQueSai.Id, terra, lugarB);
				CravarPosicao(oQueSai, lugarB);
				CravarPosicao(oQueFica, lugarC);
				Comecar(oQueSai, oQueFica, NowMs());
				if (!_emEmbate.TryGetValue(oQueSai.Id, out Embate? e)) return default;
				if (quemSaiVence) e.PtsA = 1; else e.PtsB = 1;   // o lado A e quem sai; o lado B e quem fica
				Dictionary<string, double[]> corpoDeQuemFica = FotografarCorpo(oQueFica.Combate), corpoDeQuemSai = FotografarCorpo(oQueSai.Combate);

				bool saiu = mesa.SaiDoJogo(b, contaB);
				return (saiu, mesa.NoDisco(contaB),
						Tiles(oQueFica.Pos, lugarC), MesmosMembrosDeTeste(FotografarCorpo(oQueFica.Combate), corpoDeQuemFica),
						Tiles(oQueSai.Pos, lugarB), MesmosMembrosDeTeste(FotografarCorpo(oQueSai.Combate), corpoDeQuemSai));
			}
			// O LOGIN SEGUINTE de quem saiu: a ficha diz oculto? e o poder expresso depois da conta de sempre.
			(bool Entrou, bool Oculto, double Expresso, bool NaListaDeInvisiveis) Volta()
			{
				ServerPlayer? voltou = mesa.EntraNoMundo(b, contaB, nomeB);
				if (voltou == null) return default;
				bool oculto = voltou.Ficha.isconcealed;
				voltou.Ficha.Statify();
				voltou.Ficha.PowerLevel();
				return (true, oculto, voltou.Ficha.expressedBP, _invisiveis.Contains(voltou.Id));
			}
			bool Solto(ServerPlayer p) => !p.Ficha.isconcealed && p.Combate.Stun <= 0 && !_emEmbate.ContainsKey(p.Id) && !_invisiveis.Contains(p.Id);

			// ---------------------------------------------------------------- 1 e 2: a saida de producao
			GD.Print("[saidaembate] -- 1) quem SAI esta PERDENDO o embate --");
			var perdendo = Cena(quemSaiVence: false);
			AfirmarSaida("**QUEM SAI NAO VAI PRO DISCO OCULTO**: o embate se desfaz antes do save, e ele e gravado visivel e onde estava",
					  perdendo.Saiu && perdendo.Disco is { } d1 && !d1.Ficha.isconcealed && Tiles(new Vec2(d1.X, d1.Y), lugarB) < 0.05f,
					  perdendo.Disco == null ? "sem save" : $"oculto no disco: {perdendo.Disco.Ficha.isconcealed}");
			AfirmarSaida("**QUEM FICA NAO GANHA CENA DE FECHO**: nao e teleportado pra cima de quem saiu, o corpo de quem saiu nao apanha, e ele sai do embate na hora -- visivel, sem trava",
					  perdendo.Saiu && perdendo.FicouAndou < 0.05f && perdendo.SaiuInteiro && perdendo.SaiuAndou < 0.05f && Solto(oQueFica) && _embates.Count == 0,
					  $"quem ficou andou {perdendo.FicouAndou:0.##} tile(s); o corpo de quem saiu ficou inteiro: {perdendo.SaiuInteiro}");

			GD.Print("[saidaembate] -- 2) quem SAI esta GANHANDO o embate --");
			var ganhando = Cena(quemSaiVence: true);
			AfirmarSaida("**QUEM SAI GANHANDO NAO BATE NA SAIDA**: nao e recolocado nas costas de quem fica, quem fica continua inteiro e onde estava, e solto",
					  ganhando.Saiu && ganhando.SaiuAndou < 0.05f && ganhando.FicouInteiro && ganhando.FicouAndou < 0.05f && Solto(oQueFica),
					  $"quem saiu andou {ganhando.SaiuAndou:0.##} tile(s); quem ficou inteiro: {ganhando.FicouInteiro}");
			AfirmarSaida("...e tambem vai pro disco visivel", ganhando.Disco is { } d2 && !d2.Ficha.isconcealed);

			// ---------------------------------------------------------------- 3: o login seguinte
			GD.Print("[saidaembate] -- 3) o login seguinte --");
			var volta = Volta();
			AfirmarSaida("**QUEM VOLTA TEM O PODER DELE**: a ficha nao diz oculto e o poder expresso passa de 5 (era onde o oculto vindo do disco o travava)",
					  volta.Entrou && !volta.Oculto && volta.Expresso > 5, $"oculto: {volta.Oculto}; poder expresso {volta.Expresso:0.##} (BP {BpDaBancadaDaSaidaDoEmbate:0})");

			// ---------------------------------------------------------------- 4: o save que ja estava oculto
			GD.Print("[saidaembate] -- 4) o save periodico cai no meio de um embate, o processo morre, e o login CURA --");
			if (mesa.CorpoDe(contaB) is { } noMeio)
			{
				CravarPosicao(noMeio, lugarB);
				CravarPosicao(oQueFica, lugarC);
				Comecar(noMeio, oQueFica, NowMs());
				Persistir(noMeio);   // o que o save periodico de 2 min faz com cada jogador
			}
			AfirmarSaida("(montagem) o save periodico no meio de um embate AINDA grava a ficha como ela esta -- oculta. Quem protege desse save e o login",
					  mesa.NoDisco(contaB)?.Ficha.isconcealed == true);
			Stop();   // a queda do processo: ninguem e solto, nada e gravado
			mesa.Bombeia(() => b.Caiu != null && c.Caiu != null, 4000);
			bool subiu = Start(porta);
			var curado = Volta();
			AfirmarSaida("**O SAVE QUE JA ESTAVA OCULTO SE CURA NO LOGIN**: quem volta depois da queda acorda visivel, com o poder expresso dele",
					  subiu && curado.Entrou && !curado.Oculto && curado.Expresso > 5, $"oculto: {curado.Oculto}; poder expresso {curado.Expresso:0.##}");
			ServerPlayer? ficaDeNovo = mesa.EntraNoMundo(c, contaC, nomeC);
			AfirmarSaida("(montagem) quem ficava tambem volta", ficaDeNovo != null);
			if (ficaDeNovo == null) return;
			oQueFica = ficaDeNovo;
			oQueFica.Ficha.BP = BpDaBancadaDaSaidaDoEmbate;

			// ---------------------------------------------------------------- 5: os contra-exemplos
			GD.Print("[saidaembate] -- 5) com os DEFEITOS INJETADOS: a saida de antes, e o login que nao cura --");
			SaidaDoEmbateDeAntesDeTeste = true;
			var antesPerdendo = Cena(quemSaiVence: false);
			AfirmarSaida("(defeito injetado: a saida de antes) quem sai e gravado OCULTO, e quem fica e TELEPORTADO pra cima dele e bate num corpo que esta indo embora -- a regua sabe reprovar",
					  antesPerdendo.Saiu && antesPerdendo.Disco?.Ficha.isconcealed == true && antesPerdendo.FicouAndou > 2,
					  $"oculto no disco: {antesPerdendo.Disco?.Ficha.isconcealed}; quem ficou andou {antesPerdendo.FicouAndou:0.##} tile(s)");
			var antesGanhando = Cena(quemSaiVence: true);
			AfirmarSaida("(defeito injetado: a saida de antes) quem sai GANHANDO e recolocado nas costas de quem fica pra dar o golpe de saida -- a regua sabe reprovar",
					  antesGanhando.Saiu && antesGanhando.SaiuAndou > 2 && antesGanhando.Disco?.Ficha.isconcealed == true,
					  $"quem saiu andou {antesGanhando.SaiuAndou:0.##} tile(s)");
			SaidaDoEmbateDeAntesDeTeste = false;

			LoginNaoCuraOOcultoDeTeste = true;
			var semCura = Volta();
			AfirmarSaida("(defeito injetado: o login nao cura) quem volta com o oculto do disco fica A VISTA de todos com o poder expresso PRESO EM 5 -- a regua sabe reprovar",
					  semCura.Entrou && semCura.Oculto && Math.Abs(semCura.Expresso - 5) < 1e-9 && !semCura.NaListaDeInvisiveis,
					  $"oculto: {semCura.Oculto}; poder expresso {semCura.Expresso:0.##}");
			LoginNaoCuraOOcultoDeTeste = false;
			bool saiuDeNovo = mesa.SaiDoJogo(b, contaB);
			var deVolta = Volta();
			AfirmarSaida("...e com o conserto de volta, o mesmo save se cura no login seguinte", saiuDeNovo && deVolta.Entrou && !deVolta.Oculto && deVolta.Expresso > 5,
					  $"oculto: {deVolta.Oculto}; poder expresso {deVolta.Expresso:0.##}");
		}
		catch (Exception e)
		{
			AfirmarSaida("a bancada rodou ate o fim", false, e.ToString());
		}
		finally
		{
			SaidaDoEmbateDeAntesDeTeste = LoginNaoCuraOOcultoDeTeste = false;
			mesa.Fecha();
			_saidaEmbateRodando = false;
			GD.Print($"[saidaembate] ================ {_saidaOk} OK, {_saidaFalhou} FALHA(S) ================");
		}
	}
}
