using Godot;
using Jandirus.Core.Tech;

namespace Jandirus.Server;

/// <summary>
/// O LADO DE CA DA TECLA E -- os verbos que o menu de interacao manda.
///
/// ============================ O MENU NAO E PERMISSAO ============================
/// O cliente decide QUAIS BOTOES desenhar (ele le o mesmo <see cref="Interacoes"/>), mas quem
/// decide se a acao acontece e este arquivo. Um cliente mexido manda o verbo que quiser, e sem a
/// conferencia daqui "sacar tudo" funcionaria de pe numa arvore de maca, ou a dez tiles do banco.
///
/// Sao duas perguntas, e as duas precisam ser feitas:
///   * ESTOU PERTO de alguma coisa que aceita este verbo? (o `AlcanceDeUso`, que e do servidor)
///   * ESTE VERBO PERTENCE A ESTA COISA? (o `Interacoes.Aceita`, que e a mesma tabela do menu)
/// ================================================================================
/// </summary>
public sealed partial class GameServer
{
	/// <summary>
	/// Trata os verbos que so existem por interacao com objeto. Devolve `true` quando o comando era
	/// deste canal -- inclusive quando ele foi RECUSADO, senao o funil seguiria adiante e o jogador
	/// leria "comando desconhecido" depois de ja ter lido "longe demais".
	/// </summary>
	private bool ComandoDeInteracao(ServerPlayer pl, string cmd, string arg)
	{
		switch (cmd)
		{
			case "colher": Colher(pl); return true;

			// O `estudar` e o `abrir_tech` JA EXISTIAM como verbos de tecnologia. O menu so passou a
			// ser uma segunda porta pra eles -- por isso o reenvio pro canal de sempre em vez de uma
			// copia da logica aqui. Duas copias divergem na primeira mudanca.
			// OS VERBOS DE TECNOLOGIA JA EXISTIAM -- o menu so virou uma segunda porta pra eles. Por
			// isso o reenvio pro canal de sempre em vez de uma copia da logica aqui: duas copias
			// divergem na primeira mudanca, e estas conferem raca, tecnologia e zeni.
			case "abrir_tech": ComandoDeTech(pl, "lista", ""); return true;
			case "pegar":
			case "aparafusar":
			case "lab_androide":
			case "lab_bio":
			case "androide_absorcao":
			case "androide_infinito":
			case "colher_dna":
			case "gestar":
				ComandoDeTech(pl, cmd, arg);
				return true;

			case "treinar_saco": TreinarNoSaco(pl); return true;
			case "maq_info": InfoDaMaquina(pl); return true;

			// A NAVE. Prefixo `nave_` e arquivo proprio (`GameServer.Nave.cs`) pela mesma razao do
			// banco: e um sistema com registro proprio (a nave NAO e uma `Obra` -- ver o cabecalho
			// de la) e nao meia duzia de `case`.
			//
			// ELA NAO PASSA PELO `ObraQueAceita`, e nao poderia: aquele varre `_noChao` filtrando
			// por NOME de zona, que e justamente o defeito que a nave nao pode herdar. O alcance e
			// conferido la dentro, contra a `ZoneKey` inteira.
			case "nave_usar":
			case "nave_lancar":
			case "nave_melhorar":
			case "nave_info":
			case "nave_pegar":
			case "nave_recondicionar":
			// A CAMADA 2 entra pelo MESMO canal, e nao por um paralelo: `nave_embarcar` e
			// `nave_pilotar` sao a mesma familia de "mexer com a nave" que `nave_usar`, e o despacho
			// de la ja sabe separar as tres naves. Um segundo `case` aqui pra cada verbo novo faria
			// desta lista o lugar que esquece um.
			case "nave_embarcar":
			case "nave_sair":
			case "nave_observar":
			case "nave_pilotar":
			case "nave_senha":
				return ComandoDeNave(pl, cmd, arg);

			// A PORTA DA SALA DO TEMPO. Ela e mobilia como o banco e a macieira, mas o que ela FAZ
			// e um sistema inteiro (autorizacao, recarga, lotacao, prisao) -- por isso mora em
			// arquivo proprio, pela mesma razao do banco. Ver `GameServer.SalaDoTempo.cs`.
			//
			// OS DOIS VERBOS DO GUARDIAO ENTRAM PELO MESMO CANAL de proposito: eles nao dependem
			// de estar perto da porta (o Guardiao autoriza de onde estiver, e solta quem esta a um
			// mapa de distancia), mas sao a MESMA regra -- separa-los em dois despachos seria a
			// tranca num arquivo e a chave noutro.
			case "sala_entrar":
			case "sala_regras":
			case "sala_autorizar":
			case "sala_soltar":
				return ComandoDaSalaDoTempo(pl, cmd, arg);

			default: return ComandoDeGravidade(pl, cmd, arg);
		}
	}

	// =====================================================================
	// AS MAQUINAS DE TREINO E DE CURA
	// =====================================================================
	/// <summary>
	/// QUANTO O SACO DE PANCADA MULTIPLICA O TREINO. O original diz "dobra os ganhos".
	///
	/// O BONUS E LIGADO/DESLIGADO pela PRESENCA, e nao por um estado guardado: se voce esta perto de
	/// um saco aparafusado, treinar rende o dobro. Guardar "estou treinando no saco" num campo seria
	/// um estado que fica preso quando o jogador anda pra longe.
	/// </summary>
	private const double DobraDoSaco = 2;

	private void TreinarNoSaco(ServerPlayer pl)
	{
		Obra? saco = ObraQueAceita(pl, "treinar_saco");
		if (saco == null) { Avisar(pl, "não há saco de pancada por perto."); return; }
		if (!saco.Aparafusada) { Avisar(pl, "aparafuse o saco antes de bater nele."); return; }

		Avisar(pl, "você começa a bater no saco. Enquanto estiver por perto, o treino rende o dobro.");
	}

	/// <summary>
	/// O BONUS DE TREINO DAS MAQUINAS, olhado na hora do ganho.
	///
	/// Devolve o multiplicador que o saco de pancada por perto concede -- 1 quando nao ha nenhum.
	/// Quem chama e o tique de treino, e por isso ele e barato: uma varredura das obras da zona.
	/// </summary>
	private double BonusDeTreinoPerto(ServerPlayer pl)
	{
		foreach (Obra o in _noChao)
		{
			if (!o.Zona.Equals(pl.Zone) || !o.Aparafusada) continue;
			if (o.Tipo is not ("Punching_Bag" or "Punching_Machine")) continue;
			if (Math.Abs(o.X - pl.Pos.X) > AlcanceDeUso || Math.Abs(o.Y - pl.Pos.Y) > AlcanceDeUso) continue;
			return DobraDoSaco;
		}
		return 1;
	}

	private void InfoDaMaquina(ServerPlayer pl)
	{
		Obra? m = ObraQueAceita(pl, "maq_info");
		if (m == null) { Avisar(pl, "não há máquina por perto."); return; }

		Avisar(pl, $"-- {NomeDaObra(m)} --");
		Avisar(pl, m.Aparafusada
			? "  aparafusada: está trabalhando."
			: "  solta: aparafuse pra ela funcionar.");
		Avisar(pl, m.Tipo == "Bio_Field"
			? $"  cura quem estiver a até {RaioDoCampoBio / 32} tiles."
			: "  cura quem ficar em cima dela.");
	}

	// =====================================================================
	// CURA PASSIVA -- Regenerador e Campo Bio
	// =====================================================================
	/// <summary>O regenerador cura quem esta EM CIMA: um tile de tolerancia.</summary>
	private const float RaioDoRegenerador = 32f;

	/// <summary>O campo bio cura num raio largo -- e uma torre, nao uma cama.</summary>
	private const float RaioDoCampoBio = 20 * 32f;

	private const double CuraDoRegeneradorPorSegundo = 3;
	private const double CuraDoCampoPorSegundo = 0.6;

	/// <summary>
	/// AS MAQUINAS DE CURA TRABALHAM SOZINHAS -- ninguem as "usa".
	///
	/// ELAS NAO CURAM EM COMBATE, pela mesma razao da regeneracao passiva e dos remedios: uma
	/// torre que cura no meio da briga faz a briga nao acabar. No original o regenerador tambem
	/// so pega quem esta parado em cima dele.
	/// </summary>
	private void TickDasMaquinasDeCura(double dt)
	{
		foreach (ServerPlayer pl in _players.Values)
		{
			if (pl.Ficha.dead || pl.Combate is not { } c) continue;
			if (c.EmCombate > 0 || pl.Ficha.HP >= 99.99) continue;

			double cura = 0;
			foreach (Obra o in _noChao)
			{
				if (!o.Zona.Equals(pl.Zone) || !o.Aparafusada) continue;

				(float raio, double porSegundo) = o.Tipo switch
				{
					"Regenerator" => (RaioDoRegenerador, CuraDoRegeneradorPorSegundo),
					"Bio_Field" => (RaioDoCampoBio, CuraDoCampoPorSegundo),
					_ => (0f, 0d),
				};
				if (raio <= 0) continue;

				if (Math.Abs(o.X - pl.Pos.X) > raio || Math.Abs(o.Y - pl.Pos.Y) > raio) continue;
				cura += porSegundo;
			}

			if (cura <= 0) continue;
			c.Corpo.Curar(cura * dt);
			c.SincronizarVida();
		}
	}

	/// <summary>
	/// PERTO DE QUE TIPO EU ESTOU? Devolve a obra mais proxima que aceita este verbo.
	///
	/// A busca e POR VERBO, e nao "a obra mais perto": com um banco e uma arvore lado a lado, pedir
	/// "colher" tem que achar a ARVORE, mesmo que o banco esteja um pixel mais perto.
	/// </summary>
	private Obra? ObraQueAceita(ServerPlayer pl, string verbo) => _noChao
		.Where(o => o.Zona.Equals(pl.Zone) && Interacoes.Aceita(o.Tipo, verbo))
		.OrderBy(o => (o.X - pl.Pos.X) * (o.X - pl.Pos.X) + (o.Y - pl.Pos.Y) * (o.Y - pl.Pos.Y))
		.FirstOrDefault(o => Math.Abs(o.X - pl.Pos.X) <= AlcanceDeUso
						  && Math.Abs(o.Y - pl.Pos.Y) <= AlcanceDeUso);

	// =====================================================================
	// A ARVORE DE MACA
	// =====================================================================
	/// <summary>
	/// QUANTAS MACAS UMA ARVORE CARREGA, e o quanto ela repoe de cada vez.
	///
	/// Numeros do original (`Plants.dm:59-75`): dez de teto, e a cada meio minuto ha metade de
	/// chance de brotarem mais tres. O sorteio importa -- sem ele a arvore vira uma torneira com
	/// vazao conhecida, e o jogador aprende a voltar no relogio em vez de passar por ela.
	/// </summary>
	private const int MacasPorArvore = 10;
	private const int MacasQueBrotam = 3;
	private const double ChanceDeBrotar = 0.50;
	private const long MsEntreBrotos = 50_000;

	/// <summary>
	/// QUANTAS MACAS CADA ARVORE AINDA TEM, por id de obra.
	///
	/// EM MEMORIA, e nao no `mundo.json`. Uma arvore que volta cheia depois de um reinicio e o
	/// comportamento do original (o `appleCount` do DM e `var` de objeto, e o mapa e recarregado a
	/// cada boot), e gravar isso poria estado de fruta no arquivo que guarda as CONSTRUCOES dos
	/// jogadores -- duas vidas uteis muito diferentes no mesmo arquivo.
	/// </summary>
	private readonly Dictionary<int, int> _macas = [];
	private long _proximoBroto;

	/// <summary>Quantas macas esta arvore tem agora. Arvore que nunca foi colhida esta cheia.</summary>
	private int MacasDe(int id) => _macas.TryGetValue(id, out int n) ? n : MacasPorArvore;

	private void Colher(ServerPlayer pl)
	{
		Obra? arvore = ObraQueAceita(pl, "colher");
		if (arvore == null) { Avisar(pl, "não há nada por perto pra colher."); return; }

		int restam = MacasDe(arvore.Id);
		if (restam <= 0)
		{
			Avisar(pl, "esta árvore está sem frutas. Volte daqui a pouco.");
			return;
		}

		// A MOCHILA PRIMEIRO, e a arvore depois. Se nao couber, a maca nao pode sair do galho --
		// tirar antes e descobrir que nao cabe seria destruir a fruta.
		if (!Guardar(pl, Jandirus.Core.Items.CatalogoDeItens.Maca)) return;

		_macas[arvore.Id] = restam - 1;
		Avisar(pl, $"você colhe uma maçã. {restam - 1} ainda pendem do galho.");
	}

	/// <summary>
	/// AS ARVORES REPOEM, de meio em meio minuto e por sorteio.
	///
	/// UM RELOGIO SO PRO MUNDO INTEIRO, e nao um por arvore: o original tinha um `spawn` recursivo
	/// por objeto, o que num mapa com centenas de arvores seriam centenas de laços. Aqui uma
	/// passada varre as que foram colhidas -- e so elas estao no dicionario.
	/// </summary>
	private void TickDasArvores()
	{
		long agora = NowMs();
		if (agora < _proximoBroto) return;
		_proximoBroto = agora + MsEntreBrotos;

		foreach (int id in _macas.Keys.ToList())
		{
			if (_macas[id] >= MacasPorArvore) { _macas.Remove(id); continue; }
			if (_rng.NextDouble() >= ChanceDeBrotar) continue;
			_macas[id] = Math.Min(_macas[id] + MacasQueBrotam, MacasPorArvore);
		}
	}
}
