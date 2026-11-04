using System.Text.Json;
using Godot;
using Jandirus.Core.Tech;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// BANCADA `--custosalemteste` (2026-11-01) -- o pedido 213 do dono: *"E faca custo de reviver, custo de visitar mundo dos vivos morto etc
/// ser uma config de adm do server, assim n precisando recompilar o jogo sempre q for alterar algo"*. Roda no BOOT, com um admin e um
/// morto forjados, pelas portas de producao (o verb da aba Admin, a mesa do Enma, o arquivo do servidor):
///   C1 os PADROES: sem nada mudado, os custos sao as constantes de sempre;
///   C2 a VOLTA: o admin muda o preco (`admin_custos_alem revive 350.000`) e o Enma cobra exatamente ele; quem levou Hakai paga o
///      multiplicador novo;
///   C3 o PASSE: o preco novo sai da ficha, e o prazo e o dos dias novos;
///   C4 o ARQUIVO: o `servidor.json` guarda os quatro, o boot os le de volta (de volta ao padrao, o carregar os traz), e trocar o modo das
///      racas nao os apaga;
///   C5 o valor fora dos limites e RECUSADO com o motivo, e nada muda;
///   C6 o botao da mesa do Enma escreve os numeros novos.
///
///     Godot --headless --path . --server --port 12821 --custosalemteste
/// </summary>
public sealed partial class GameServer
{
	private int _custoOk, _custoFalhou;
	private const int IdBaseDosCustosDoAlemDeTeste = 95_000;

	private void AfirmarCusto(string oque, bool passou, string detalhe = "")
	{
		if (passou) { _custoOk++; GD.Print($"[custosalem]   OK    {oque}   [{detalhe}]"); return; }
		_custoFalhou++;
		GD.PrintErr($"[custosalem]   FALHA {oque}   {detalhe}");
	}

	public void RodarBancadaDosCustosDoAlem()
	{
		_custoOk = _custoFalhou = 0;
		GD.Print("[custosalem] ================ OS CUSTOS DO OUTRO MUNDO SAO DO SERVIDOR (dono, 2026-11-01) ================");

		string caminho = Path.Combine(_store?.Pasta ?? PastaDosSaves(), NomeDoArquivoDoServidor);
		string? arquivoAntes = File.Exists(caminho) ? File.ReadAllText(caminho) : null;
		ZoneKey terra = ZoneKey.Premade("Earth");
		var corpos = new List<ServerPlayer>();
		ServerPlayer Forjar(int i, string nome)
		{
			var p = new ServerPlayer
			{
				Id = IdBaseDosCustosDoAlemDeTeste + i, Peer = null, Name = nome, Race = "Human", Genero = "Male", Idade = 25, Zone = terra,
				Pos = new Vec2(i * 3 * ZoneCollision.TileSize, 0), Conta = $"bancada_custosalem_{i}", Slot = 0,
				Ficha = new Jandirus.Core.Stats.Fighter { Race = "Human", BP = 10_000 }, Livro = new Jandirus.Core.Skills.SkillBook(),
			};
			p.Ficha.Class = "Normal";
			PorNoMundo(p);
			corpos.Add(p);
			return p;
		}

		try
		{
			// ---- C1) os padroes
			CustosDoAlem.Padrao();
			AfirmarCusto("C1. os PADROES sao as constantes de sempre (200 mil a volta, Hakai 2x, 100 mil o passe por 7 dias)",
					  CustosDoAlem.PrecoDoRevive == Alem.PrecoDoReviveDoEnma && CustosDoAlem.PrecoDoPasse == Alem.PrecoDoPasseDoEnma
					  && CustosDoAlem.DiasDoPasse == Alem.DiasDoPasseDoEnma
					  && CustosDoAlem.MultiplicadorDoHakai == Jandirus.Core.Ranks.KitDoDeus.HakaiPrecoDoRevive, CustosDoAlem.Resumo());

			// ---- C2) a volta paga
			ServerPlayer adm = Forjar(1, "bancada: o admin");
			ServerPlayer morto = Forjar(2, "bancada: o morto");
			List<string> mudou = Ouvir(() =>
			{
				AdminCustosDoAlem(adm, "revive 350.000");
				AdminCustosDoAlem(adm, "hakai 3");
			});
			bool aoLado = PalcoV30_MortoAoLadoDoEnma(morto.Id, 1_000_000);
			EnmaReviverPorZeni(morto);
			double pagou = 1_000_000 - morto.Ficha.Zeni;
			ServerPlayer apagado = Forjar(3, "bancada: o apagado por Hakai");
			apagado.Ficha.hakai_mark = true;
			double precoDoApagado = PrecoDoEnmaPara(apagado);
			AfirmarCusto("C2. a VOLTA: o admin muda o preco pelo verb da aba Admin e o Enma cobra EXATAMENTE ele; quem levou Hakai paga o multiplicador novo",
					  aoLado && !morto.Ficha.dead && Math.Abs(pagou - 350_000) < 0.5 && Math.Abs(precoDoApagado - 1_050_000) < 0.5
					  && mudou.Any(s => s.Contains("350.000")),
					  $"ao lado {aoLado}; vivo {!morto.Ficha.dead}; pagou {pagou:N0}; o apagado {precoDoApagado:N0}; {string.Join(" | ", mudou)}");

			// ---- C3) o passe
			AdminCustosDoAlem(adm, "passe 50000");
			AdminCustosDoAlem(adm, "dias 3");
			ServerPlayer visitante = Forjar(4, "bancada: quem visita");
			PalcoV30_MortoAoLadoDoEnma(visitante.Id, 1_000_000);
			long antes = NowMs();
			EnmaPasse(visitante, "Earth");
			double pagouOPasse = 1_000_000 - visitante.Ficha.Zeni;
			long prazo = visitante.Ficha.passe_do_enma_ate - antes;
			long tresDias = (long)(3 * Espaco.SegundosPorDiaInGame * 1000);
			AfirmarCusto("C3. o PASSE: o preco novo sai da ficha, e o prazo e o dos DIAS novos",
					  Math.Abs(pagouOPasse - 50_000) < 0.5 && Math.Abs(prazo - tresDias) < 5_000,
					  $"pagou {pagouOPasse:N0}; prazo {prazo / 1000.0:0} s (3 dias = {tresDias / 1000.0:0} s)");

			// ---- C4) o arquivo
			string gravado = File.Exists(caminho) ? File.ReadAllText(caminho) : "";
			Jandirus.Core.Races.ModoDeRacas modo = ModoDeRacas;
			EscolherModoDeRacas(modo, "a bancada dos custos");   // o modo das racas grava o mesmo arquivo: ele nao pode apagar os custos
			CustosDoAlem.Padrao();
			CarregarConfiguracaoDoServidor();
			AfirmarCusto("C4. o ARQUIVO: o servidor.json guarda os quatro, o boot os le de volta, e gravar o modo das racas nao os apaga",
					  CustosDoAlem.PrecoDoRevive == 350_000 && CustosDoAlem.MultiplicadorDoHakai == 3 && CustosDoAlem.PrecoDoPasse == 50_000
					  && CustosDoAlem.DiasDoPasse == 3 && ModoDeRacas == modo,
					  $"depois do carregar: {CustosDoAlem.Resumo()}; o arquivo: {gravado.Replace('\n', ' ')}");

			// ---- C5) fora dos limites
			string antesDaRecusa = CustosDoAlem.Resumo();
			List<string> recusas = Ouvir(() =>
			{
				AdminCustosDoAlem(adm, "dias 0");
				AdminCustosDoAlem(adm, "revive -5");
				AdminCustosDoAlem(adm, "hakai 0,5");
				AdminCustosDoAlem(adm, "lua 10");
			});
			AfirmarCusto("C5. o valor fora dos limites e RECUSADO com o motivo, e nada muda",
					  CustosDoAlem.Resumo() == antesDaRecusa && recusas.Count(s => s.Contains("inválido") || s.Contains("não conheço")) == 4,
					  string.Join(" | ", recusas));

			// ---- C6) o botao da mesa
			Interacoes.Acao[] mesa = Interacoes.De(Alem.TipoDoEnma);
			string volta = mesa.FirstOrDefault(a => a.Verbo == "enma_reviver").Rotulo ?? "";
			string passe = mesa.FirstOrDefault(a => a.Verbo == "enma_passe").Rotulo ?? "";
			AfirmarCusto("C6. o BOTAO da mesa do Enma escreve os numeros novos",
					  volta == "Voltar à vida (350.000 zeni)" && passe == "Visitar os vivos (50.000 zeni, 3 dias)", $"'{volta}' / '{passe}'");
		}
		finally
		{
			// O ARQUIVO VOLTA AO QUE ERA (ou some, se nao havia), e os custos e o modo com ele
			try
			{
				if (arquivoAntes != null) File.WriteAllText(caminho, arquivoAntes);
				else if (File.Exists(caminho)) File.Delete(caminho);
			}
			catch (Exception e) { GD.PushWarning($"[custosalem] nao devolvi o {NomeDoArquivoDoServidor}: {e.Message}"); }
			CustosDoAlem.Padrao();
			CarregarConfiguracaoDoServidor();
			foreach (ServerPlayer p in corpos)
			{
				_players.Remove(p.Id);
				ZoneList(p.Zone.Hash).Remove(p);
			}
		}
		GD.Print($"[custosalem] ================ {_custoOk} passaram, {_custoFalhou} falharam ================");
	}
}
