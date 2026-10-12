using Godot;
using Jandirus.Core.Items;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// A BANCADA DO SAQUE (`--saqueteste`) -- o item 9b do playtest de 2026-10-10: *"jogadores podem pegar itens de
/// personagens nocauteados (jogadores e NPCs) interagindo com o corpo. Padrao: siga o BYOND se ele tinha isso;
/// senao, so itens do inventario, nao os equipados."*
///
/// No boot, como as outras de servidor. Os corpos sao forjados (sem `Peer`) e TUDO passa pelo funil de
/// producao: o verbo `saquear` entra pelo `Verbo(...)` -- a mesma porta do `C2S.Verbo` --, e a lista que o
/// servidor mandaria pelo fio e lida pela <see cref="EscutaDeSaque"/>, que recebe o mesmo que vai no pacote.
///
///   1. A REGRA (Core): o que sai e o que fica -- o scouter LIGADO e os pesos NO CORPO ficam.
///   2. A PERGUNTA: `saquear` sem argumento devolve a lista de quem esta caido ao alcance.
///   3. O ITEM: um por clique, de uma mochila pra outra, anunciado a quem esta perto, com o respiro.
///   4. AS RECUSAS: de pe, longe, morto, ladrao caido, mochila cheia, item que ja saiu.
///   5. O NPC: cai na mesma regra e, pelado, nao tem o que levar (nenhum molde nasce com item). A ROUPA DO CORPO
///      -- que passou a sair em 2026-10-12, armadura inclusive -- e medida na `--v020teste`, familia 15.
///
/// O defeito injetado (`Saque.EquipadoTambemSaiDeTeste`) e a regra sem a ressalva do equipado.
///
///     Godot --headless -- --server --port 7996 --saqueteste
/// </summary>
public partial class GameServer
{
	private int _sqOk, _sqFalhou;

	private void AfirmarSq(string nome, bool cond, string detalhe = "")
	{
		if (cond) { _sqOk++; GD.Print($"[saque]   OK    {nome}" + (detalhe.Length > 0 ? $"   [{detalhe}]" : "")); }
		else { _sqFalhou++; GD.PrintErr($"[saque]   FALHA {nome}   [{detalhe}]"); }
	}

	public void RodarBancadaDoSaque()
	{
		_sqOk = _sqFalhou = 0;
		_pjProximoCorredor = 8;
		_pjMapa ??= MapaDaZonaOuCatalogo(ZonaDaBancadaDeProjetil);
		GD.Print("[saque] ================ O SAQUE DE QUEM ESTA NOCAUTEADO (playtest, 2026-10-10) ================");

		// A ULTIMA LISTA QUE O SERVIDOR MONTOU pro ladrao -- o conteudo do `S2C.Saque`.
		(int Ladrao, int Caido, List<Pilha> Leva)? lista = null;
		EscutaDeSaque = (ladrao, caido, leva) => lista = (ladrao, caido, [.. leva]);

		try
		{
			// ---------------------------------------------------------------- 1. a regra
			GD.Print("[saque] -- 1) a regra: o que sai e o que fica --");
			var mochila = new Inventario();
			mochila.Guardar(CatalogoDeItens.Maca, 3);
			mochila.Guardar(CatalogoDeItens.Scouter);
			mochila.Guardar(CatalogoDeItens.Pesos);
			List<Pilha> tudo = Saque.OQueDaPraLevar(mochila, scouterLigado: false, comPesos: false);
			List<Pilha> emUso = Saque.OQueDaPraLevar(mochila, scouterLigado: true, comPesos: true);
			AfirmarSq("guardado, tudo sai: a maca, o scouter desligado e os pesos fora do corpo", tudo.Count == 3, $"{tudo.Count}");
			AfirmarSq("**O QUE ESTA EM USO FICA**: com o scouter ligado e os pesos no corpo, so a maca sai",
					  emUso.Count == 1 && emUso[0].Id == CatalogoDeItens.Maca && emUso[0].Quantidade == 3,
					  string.Join(",", emUso.Select(p => p.Id)));

			// ---------------------------------------------------------------- a cena
			Vec2 praca = CorredorLivre(6);
			ServerPlayer ladrao = Forjar("Ladrao", praca, 1000);
			ServerPlayer vitima = Forjar("Vitima", praca + new Vec2(ZoneCollision.TileSize, 0), 1000);
			vitima.Mochila.Guardar(CatalogoDeItens.Maca, 3);
			vitima.Mochila.Guardar(CatalogoDeItens.SementeDeMaca, 1);
			vitima.Mochila.Guardar(CatalogoDeItens.Scouter);
			vitima.Mochila.Guardar(CatalogoDeItens.Pesos);
			vitima.PoderesConcedidos |= Protocol.Poder.Scouter;   // o scouter LIGADO
			vitima.Ficha.Weighted = 10;                            // os pesos NO CORPO

			// ---------------------------------------------------------------- 4a. de pe nao se saqueia
			GD.Print("[saque] -- 2) a pergunta, e quem pode ser revistado --");
			lista = null;
			List<string> falas = Ouvir(() => Verbo(ladrao, Saque.Verbo, ""));
			AfirmarSq("DE PE ninguem e revistado: o verbo recusa e nao manda lista nenhuma",
					  lista == null && Disse(falas, "nocauteado"), Ultimos(falas));

			vitima.Combate.Nocautear(60);
			lista = null;
			Ouvir(() => Verbo(ladrao, Saque.Verbo, ""));
			AfirmarSq("**NOCAUTEADO E AO ALCANCE: O SERVIDOR RESPONDE A LISTA** -- a maca e a semente, sem o scouter ligado nem os pesos do corpo",
					  lista is { } l1 && l1.Ladrao == ladrao.Id && l1.Caido == vitima.Id && l1.Leva.Count == 2
					  && l1.Leva.Exists(p => p.Id == CatalogoDeItens.Maca && p.Quantidade == 3)
					  && l1.Leva.Exists(p => p.Id == CatalogoDeItens.SementeDeMaca),
					  lista is { } l1b ? string.Join(",", l1b.Leva.Select(p => $"{p.Id}x{p.Quantidade}")) : "sem lista");

			// ---------------------------------------------------------------- 3. o item
			GD.Print("[saque] -- 3) levar um item --");
			falas = Ouvir(() => Verbo(ladrao, Saque.Verbo, CatalogoDeItens.Maca));
			AfirmarSq("**UM POR CLIQUE, DE UMA MOCHILA PRA OUTRA**: a vitima fica com 2 macas e o ladrao com 1",
					  vitima.Mochila.Quantos(CatalogoDeItens.Maca) == 2 && ladrao.Mochila.Quantos(CatalogoDeItens.Maca) == 1,
					  $"vitima {vitima.Mochila.Quantos(CatalogoDeItens.Maca)}, ladrao {ladrao.Mochila.Quantos(CatalogoDeItens.Maca)}");
			AfirmarSq("...e a lista nova vai junto (a maca agora com 2)",
					  lista is { } l2 && l2.Leva.Exists(p => p.Id == CatalogoDeItens.Maca && p.Quantidade == 2));

			falas = Ouvir(() => Verbo(ladrao, Saque.Verbo, CatalogoDeItens.Maca));
			AfirmarSq("O RESPIRO: o clique seguinte, dentro de um segundo, nao leva nada (e avisa)",
					  vitima.Mochila.Quantos(CatalogoDeItens.Maca) == 2 && Disse(falas, "um de cada vez"), Ultimos(falas));
			ladrao.ProximoSaqueMs = 0;

			falas = Ouvir(() => Verbo(ladrao, Saque.Verbo, CatalogoDeItens.Scouter));
			AfirmarSq("o scouter LIGADO nao sai nem pedido pelo nome: o servidor confere de novo, e nao a lista do cliente",
					  vitima.Mochila.Quantos(CatalogoDeItens.Scouter) == 1 && ladrao.Mochila.Quantos(CatalogoDeItens.Scouter) == 0
					  && Disse(falas, "não está mais"), Ultimos(falas));

			Saque.EquipadoTambemSaiDeTeste = true;
			try
			{
				ladrao.ProximoSaqueMs = 0;
				Ouvir(() => Verbo(ladrao, Saque.Verbo, CatalogoDeItens.Scouter));
				AfirmarSq("(defeito injetado: o equipado tambem sai) o scouter ligado troca de mochila -- a regua sabe reprovar",
						  vitima.Mochila.Quantos(CatalogoDeItens.Scouter) == 0 && ladrao.Mochila.Quantos(CatalogoDeItens.Scouter) == 1);
			}
			finally { Saque.EquipadoTambemSaiDeTeste = false; }

			// ---------------------------------------------------------------- 4. as recusas
			GD.Print("[saque] -- 4) as recusas --");
			ladrao.ProximoSaqueMs = 0;
			// A MOCHILA CHEIA e montada na mao (30 slots ocupados): o que se mede e a recusa, nao o caminho ate ela.
			while (ladrao.Mochila.Pilhas.Count < Inventario.Slots) ladrao.Mochila.Pilhas.Add(new Pilha(CatalogoDeItens.Pa, 1));
			falas = Ouvir(() => Verbo(ladrao, Saque.Verbo, CatalogoDeItens.SementeDeMaca));
			AfirmarSq("MOCHILA CHEIA: a semente fica com a vitima (primeiro cabe, depois sai)",
					  ladrao.Mochila.Cheio && vitima.Mochila.Quantos(CatalogoDeItens.SementeDeMaca) == 1 && Disse(falas, "cheia"), Ultimos(falas));
			ladrao.Mochila.Pilhas.Clear();
			ladrao.ProximoSaqueMs = 0;

			Vec2 perto = ladrao.Pos;
			ladrao.Pos = vitima.Pos + new Vec2(ZoneCollision.TileSize * 6, 0);
			lista = null;
			falas = Ouvir(() => Verbo(ladrao, Saque.Verbo, CatalogoDeItens.Maca));
			AfirmarSq("LONGE (6 tiles): nada sai e nao ha lista", lista == null && vitima.Mochila.Quantos(CatalogoDeItens.Maca) == 2, Ultimos(falas));
			ladrao.Pos = perto;

			ladrao.Combate.Nocautear(60);
			falas = Ouvir(() => Verbo(ladrao, Saque.Verbo, CatalogoDeItens.Maca));
			AfirmarSq("LADRAO NOCAUTEADO nao revista ninguem", vitima.Mochila.Quantos(CatalogoDeItens.Maca) == 2 && Disse(falas, "caído"), Ultimos(falas));
			ladrao.Combate.Levantar();

			vitima.Combate.Levantar();
			lista = null;
			Ouvir(() => Verbo(ladrao, Saque.Verbo, CatalogoDeItens.Maca));
			AfirmarSq("A VITIMA LEVANTOU: acabou o saque (o estado e conferido a cada clique, e nao so na abertura)",
					  lista == null && vitima.Mochila.Quantos(CatalogoDeItens.Maca) == 2);

			// ---------------------------------------------------------------- 5. o NPC
			GD.Print("[saque] -- 5) o NPC --");
			ServerPlayer npc = Forjar("Cidadao", praca + new Vec2(0, ZoneCollision.TileSize), 500);
			npc.Combate.Nocautear(60);
			vitima.Pos = praca + new Vec2(ZoneCollision.TileSize * 8, 0);
			lista = null;
			Ouvir(() => Verbo(ladrao, Saque.Verbo, ""));
			AfirmarSq("O NPC NOCAUTEADO cai na MESMA regra -- e, sem roupa no corpo, a lista vem vazia: nenhum molde deste port carrega item",
					  lista is { } l5 && l5.Caido == npc.Id && l5.Leva.Count == 0);
		}
		catch (Exception e)
		{
			AfirmarSq("a bancada rodou ate o fim", false, e.ToString());
		}
		finally
		{
			Saque.EquipadoTambemSaiDeTeste = false;
			EscutaDeSaque = null;
			LimparTudoDaBancada();
		}

		GD.Print($"[saque] ================ {_sqOk} OK, {_sqFalhou} FALHA(S) ================");
	}
}
