using Godot;
using Jandirus.Core.Forms;
using Jandirus.Core.Social;
using Jandirus.Core.World;
using Jandirus.Net;
using LiteNetLib.Utils;

namespace Jandirus.Server;

/// <summary>
/// BANCADA AO VIVO DO CONVIVIO -- roda dentro do `--formasteste` e, sozinha, pela `--conhecidosteste`.
///
/// ============================ O QUE SO DAQUI SE VE ============================
/// As REGRAS do convivio sao do Core (`Convivio`), e a bancada do Core (`convivio`) prova todas elas com
/// dicionarios na mao. O que ela **nao** consegue provar e a CORRENTE, que e onde este port ja quebrou
/// muitas vezes:
///
///   conviver -> **DECLARAR amigo** -> ver o amigo morrer -> entrar em furia -> **a tecla C sair da base**.
///
/// Cada elo mora num arquivo diferente (Core, `GameServer.Convivio.cs`, `GameServer.Formas.cs`), e
/// os tres podem estar certos com a corrente arrebentada no meio.
///
/// E, DESDE 2026-10-10, O ELO DO MEIO E DO JOGADOR: a afinidade automatica saiu (pedido do dono), e quem
/// faz de alguem um amigo e a declaracao. Por isso a primeira coisa que esta bancada mede e o AVESSO -- com
/// todo o convivio pago e nenhuma declaracao, a morte nao enfurece ninguem --, e so depois a corrente.
/// ==============================================================================
///
/// ============================ OS CORPOS SAO FORJADOS ============================
/// Sete corpos inventados, numa zona pre-feita onde nao ha ninguem conectado -- o mesmo padrao do
/// <see cref="MedirCongelamentoNaCena"/>, e pelo mesmo motivo: isto mata gente, poe gente em furia
/// e mexe em forma, e quem esta rodando a bancada tem que terminar como comecou.
///
/// A DIFERENCA E QUE ELES ENTRAM NO `_players` E NA `ZoneList`, e precisam entrar: o luto varre a
/// zona, e um corpo fora da lista da zona nao e visto por ninguem -- a bancada daria verde por
/// ausencia. Entram e saem DENTRO do mesmo bloco sincrono, entao nenhum tique do jogo chega a ve-los.
/// ================================================================================
/// </summary>
public partial class GameServer
{
	/// <summary>Faixa de ids de bancada -- bem longe do `_nextId`, que comeca em 1.</summary>
	private const int IdBaseDoConvivioDeTeste = 90_100;

	/// <summary>
	/// `--conhecidosteste`: SO a bancada do convivio, no boot e com placar proprio -- a mesma
	/// <see cref="OConvivioAoVivo"/> que a `--formasteste` chama no meio das mil checagens dela. Existe pra
	/// quem mexe na aba People poder cobrar o servidor em segundos.
	/// </summary>
	public void RodarBancadaDosConhecidos()
	{
		int ok = 0, falhou = 0;
		GD.Print("[conhecidos] ================ A ABA PEOPLE NO SERVIDOR: CONVIVIO E DECLARACAO ================");
		try
		{
			OConvivioAoVivo((nome, cond, detalhe) =>
			{
				if (cond) { ok++; GD.Print($"[conhecidos]   OK    {nome}"); }
				else { falhou++; GD.PrintErr($"[conhecidos]   FALHA {nome}   {detalhe}"); }
			});
		}
		catch (Exception e)
		{
			falhou++;
			GD.PrintErr($"[conhecidos]   FALHA a bancada rodou inteira (estourou: {e.Message})   {e.StackTrace}");
		}
		finally
		{
			GD.Print($"[conhecidos] ================ {ok} OK, {falhou} FALHA(S) ================");
		}
	}

	/// <summary>
	/// SO PRA BANCADA: `quem` passa a ter `alvo` na lista, DECLARADO como `relacao` -- direto na ficha, sem
	/// passar pelo portao de convivio. E o que as bancadas de OUTROS sistemas (a mente, a absorcao do Majin)
	/// usam pra dizer "estes dois sao amigos": o que elas medem e o que a furia faz, e nao quanto custa
	/// declarar. Quem mede o portao e esta bancada, pelo verb.
	/// </summary>
	internal void DeclararDeTeste(ServerPlayer quem, ServerPlayer alvo, Relacao relacao)
	{
		Fotografar(quem, alvo, 0);
		if (quem.Social.Ficha(alvo.Assinatura) is { } ficha) ficha.Relacao = relacao;
	}

	/// <summary>
	/// SO PRA BANCADA DE JANELA (`--diagconhecidos`): poe estas fichas na lista de um jogador LOGADO e manda a
	/// lista pelo fio de verdade. O convivio real leva dezenas de minutos ao lado de outro JOGADOR (NPC nao
	/// entra na lista de ninguem), e a bancada nasce sozinha -- entao o elenco e plantado AQUI, do lado do
	/// servidor, pra que tudo o que vem depois (a lista que chega, o clique na regua, o verb `relacao`, a
	/// recusa do portao, a lista de volta) seja o caminho de producao.
	/// </summary>
	internal bool PlantarConhecidosDeTeste(int id, IEnumerable<Conhecido> elenco)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return false;
		long agora = NowMs();
		foreach (Conhecido c in elenco)
		{
			c.VistoEm = agora;
			pl.Social.Conhecidos[c.Assinatura] = c;
		}
		MandarConhecidos(pl);
		return true;
	}

	/// <summary>SO PRA BANCADA DE JANELA: o que o SERVIDOR guarda desta pessoa na lista do jogador (nulo = nao conhece).</summary>
	internal (int Convivio, Relacao Declaracao)? ConhecidoDeTeste(int id, string assinatura) =>
		_players.TryGetValue(id, out ServerPlayer? pl) && pl.Social.Ficha(assinatura) is { } c
			? (c.Familiaridade, c.Relacao)
			: null;

	private void OConvivioAoVivo(Action<string, bool, string> Checa)
	{
		// A ZONA VAZIA: um planeta pre-feito onde nao ha ninguem conectado, pra que o anuncio da
		// transformacao e as frases de raiva nao cheguem a tela de ninguem.
		ZoneKey zona = ZoneKey.Premade(
			Espaco.PreFeitos().Select(p => p.Nome)
				.FirstOrDefault(n => !_players.Values.Any(
					p => string.Equals(p.Zone.Name, n, StringComparison.OrdinalIgnoreCase)))
			?? "Namek");

		ServerPlayer Forjar(int i, string nome, float x)
		{
			var novo = new ServerPlayer
			{
				Id = IdBaseDoConvivioDeTeste + i,
				Peer = null,
				Name = nome,
				Race = "Saiyan",
				Genero = "Male",
				Idade = 25,
				Zone = zona,
				Pos = new Vec2(x, 0),
				// A CONTA E O SLOT SAO O QUE DAO ASSINATURA a este corpo -- sem eles ele e um NPC
				// aos olhos do convivio (ver `EhPessoa`) e a bancada mediria o nada.
				Conta = $"bancada_convivio_{i}",
				Slot = 0,
				Ficha = new Jandirus.Core.Stats.Fighter { Race = "Saiyan", BP = 3_000_000 },
			};
			novo.Ficha.Class = "Normal";
			// A SEQUENCIA DE POR UM CORPO NO MUNDO virou funcao unica (`PorNoMundo`,
			// GameServer.Npc.cs) -- era escrita aqui e no clone da mente, e a fabrica de NPC seria a
			// terceira. O `Ki` cheio vem DEPOIS dela porque so ali o `MaxKi` ja existe.
			PorNoMundo(novo);
			novo.Ficha.Ki = novo.Ficha.MaxKi;
			return novo;
		}

		// amigo e vitima colados (2 tiles); o algoz ao lado; a testemunha DISTANTE a 40 tiles --
		// fora do `RaioDeTestemunha` (22), que e o que prova o "na frente dele".
		ServerPlayer amigo = Forjar(1, "bancada: o enlutado", 0);
		ServerPlayer vitima = Forjar(2, "bancada: o amigo", 2 * ZoneCollision.TileSize);
		ServerPlayer algoz = Forjar(3, "bancada: o algoz", 3 * ZoneCollision.TileSize);
		ServerPlayer longe = Forjar(4, "bancada: o distante", 40 * ZoneCollision.TileSize);
		// O QUINTO CORPO E DA LINHA LENDARIA, e ele existe porque o desconto do dono ("o Legendary
		// precisa de MENOS: basta ver um amigo ser NOCAUTEADO") so vira regra quando alguem sobe com
		// um nocaute AO LADO de quem nao sobe com o mesmo nocaute. Um corpo so nao mede diferenca.
		ServerPlayer lenda = Forjar(5, "bancada: o lendario", 1 * ZoneCollision.TileSize);
		// O SEXTO E O SETIMO SAO DAS MUTACOES (secao 10) e vivem LONGE dos outros de proposito: elas
		// matam gente varias vezes pra medir o mesmo criterio tres vezes, e a 100 tiles ninguem entra
		// no `RaioDeTestemunha` (22) de ninguem -- senao as mortes da secao 10 mexeriam no que as
		// secoes 1-9 acabaram de medir.
		ServerPlayer cobaiaA = Forjar(6, "bancada: a cobaia", 100 * ZoneCollision.TileSize);
		ServerPlayer cobaiaB = Forjar(7, "bancada: o carrasco", 101 * ZoneCollision.TileSize);

		// O QUE O SERVIDOR DIZ, guardado: o aviso da porta do amigo e o do save antigo sao frases, e frase
		// que nao sai nao quebra nada. A escuta de quem chamou (a `--formasteste` usa a mesma) volta no fim.
		List<string>? escutaDeFora = EscutaDeAvisos;
		var avisos = new List<string>();
		EscutaDeAvisos = avisos;

		try
		{
			// ============================ 1. CONVIVER RENDE CONVIVIO -- E SO CONVIVIO ============================
			// O relogio e o que era da afinidade: 500 passos de 3 s = 25 minutos a menos de 6 tiles pagam o
			// "amigo". O que esta secao mede e o DEGRAU (499 passos ainda nao pagam) e, logo depois, o pedido
			// do dono de 2026-10-10: **pago o convivio, o jogo nao conclui nada** -- sem declaracao, ver a
			// pessoa morrer nao enfurece.
			//
			// O PRAZO E PUXADO PRA TRAS a cada volta em vez de esperar 3 s de bancada. E o mesmo
			// gesto que a bancada da raiva usa com as janelas de furia.
			void UmPasso()
			{
				amigo.ProximaAproximacao = 0;
				vitima.ProximaAproximacao = 0;
				longe.ProximaAproximacao = 0;
				algoz.ProximaAproximacao = 0;
				TickDoConvivio();
			}

			for (int i = 0; i < 9; i++) UmPasso();
			Checa("nove passos (27 s) lado a lado: os dois ja se conhecem, e o convivio ainda e zero",
				  amigo.Social.Conhece(vitima.Assinatura) && amigo.Social.Familiaridade(vitima.Assinatura) == 0,
				  $"{amigo.Social.Familiaridade(vitima.Assinatura)}");
			UmPasso();
			Checa("**no decimo passo (30 s) entra o primeiro ponto de convivio, nos dois lados**",
				  amigo.Social.Familiaridade(vitima.Assinatura) == 1 && vitima.Social.Familiaridade(amigo.Assinatura) == 1,
				  $"{amigo.Social.Familiaridade(vitima.Assinatura)} / {vitima.Social.Familiaridade(amigo.Assinatura)}");

			for (int i = 10; i < 499; i++) UmPasso();
			int conv = amigo.Social.Familiaridade(vitima.Assinatura);
			VerboRelacao(amigo, $"{vitima.Assinatura}|amigo");
			Checa("**um passo antes dos 25 minutos o convivio e 49, e dizer 'amigo' ainda e RECUSADO**",
				  conv == 49 && amigo.Social.RelacaoCom(vitima.Assinatura) == Relacao.Nenhuma,
				  $"convivio {conv}, declaracao {amigo.Social.RelacaoCom(vitima.Assinatura)}");
			Checa("...e ate aqui o servidor nao avisou porta nenhuma",
				  !avisos.Any(a => a.Contains("já conviveu")), string.Join(" | ", avisos.Where(a => a.Contains("conviveu"))));

			UmPasso();
			Checa("**no passo 500 (25 min a menos de 6 tiles) o convivio chega a 50, nos dois lados**",
				  amigo.Social.Familiaridade(vitima.Assinatura) == 50 && vitima.Social.Familiaridade(amigo.Assinatura) == 50,
				  $"{amigo.Social.Familiaridade(vitima.Assinatura)} / {vitima.Social.Familiaridade(amigo.Assinatura)}");
			// A FRASE QUE OCUPA O LUGAR DO ANTIGO "voce o considera um amigo". Quem convive com a vitima a
			// menos de 6 tiles sao DOIS corpos (o amigo e o algoz; o lendario nao da passo aqui), entao sao
			// duas frases com o nome dela -- uma pra cada um, no passo em que cada um atravessou os 50.
			int sobreAVitima = avisos.Count(a => a.Contains($"já conviveu com {vitima.Name}"));
			Checa("...e o servidor diz a cada um que ja da pra declarar -- e diz que a escolha e do JOGADOR",
				  sobreAVitima == 2 && avisos.All(a => !a.Contains("já conviveu") || a.Contains("A escolha é sua")),
				  $"{sobreAVitima} aviso(s) sobre a vitima, {avisos.Count(a => a.Contains("já conviveu"))} ao todo");
			int aoTodo = avisos.Count(a => a.Contains("já conviveu"));
			for (int i = 0; i < 10; i++) UmPasso();
			Checa("...UMA vez por pessoa: mais um ponto de convivio (51) nao repete o aviso de ninguem",
				  amigo.Social.Familiaridade(vitima.Assinatura) == 51
				  && avisos.Count(a => a.Contains("já conviveu")) == aoTodo,
				  $"{avisos.Count(a => a.Contains("já conviveu"))} (eram {aoTodo})");
			Checa("...e quem estava a 40 tiles nao conheceu ninguem",
				  longe.Social.Conhecidos.Count == 0, $"{longe.Social.Conhecidos.Count}");
			Checa("...e a ficha do conhecido guarda a raca de quando ele foi visto",
				  amigo.Social.Ficha(vitima.Assinatura)?.Raca == "Saiyan",
				  amigo.Social.Ficha(vitima.Assinatura)?.Raca ?? "sem ficha");

			// ---- O AVESSO, ANTES DE QUALQUER DECLARACAO ----
			// Na afinidade, este era o instante em que o jogo dizia "voces sao amigos" e a morte de um passava
			// a enfurecer o outro. Hoje ele nao diz nada: a vitima morre nas maos de um inimigo, na frente de
			// quem conviveu 25 minutos com ela, e ninguem se enfurece -- porque ninguem DISSE que ela era amiga.
			amigo.FuriaExtremaAte = 0;
			amigo.RaivaComumAte = 0;
			Checa("**com o convivio pago e NENHUMA declaracao, o jogo nao concluiu amizade nenhuma**",
				  !amigo.Social.EhAmigo(vitima.Assinatura) && !vitima.Social.EhAmigo(amigo.Assinatura), "");
			AoPerderALuta(vitima, algoz, morreu: true);
			Checa("**...e ve-la MORRER nas maos de um inimigo nao enfurece quem nunca disse que ela era amiga**",
				  Perfil(amigo).Raiva == NivelDeRaiva.Nenhuma, Perfil(amigo).Raiva.ToString());

			// ============================ 2. QUEM FAZ O AMIGO E A DECLARACAO, PELO VERB ============================
			// O caminho do jogador: o botao da aba People manda `relacao <assinatura>|<nome>`, e o servidor
			// confere o convivio de novo (`Convivio.Declarar`).
			VerboRelacao(amigo, $"{vitima.Assinatura}|amigo");
			Checa("com 50 de convivio, o verb `relacao ...|amigo` passa",
				  amigo.Social.RelacaoCom(vitima.Assinatura) == Relacao.Bom,
				  amigo.Social.RelacaoCom(vitima.Assinatura).ToString());
			Checa("...e so agora ela e AMIGA -- e so pra quem declarou (a declaracao e de mao unica)",
				  amigo.Social.EhAmigo(vitima.Assinatura) && !vitima.Social.EhAmigo(amigo.Assinatura), "");

			// ============================ 3. DUELO ENTRE AMIGOS NAO ENFURECE NINGUEM ============================
			// A trava que impede o treino de virar fabrica de Super Saiyajin (`Death.dm:75`). Aqui a vitima
			// DECLAROU o algoz amigo -- e ninguem entra em luto, mesmo com o amigo dela assistindo.
			VerboRelacao(vitima, $"{algoz.Assinatura}|amigo");
			Checa("(preparo) a vitima declara o algoz amigo -- o convivio dos dois ja paga",
				  vitima.Social.RelacaoCom(algoz.Assinatura) == Relacao.Bom,
				  $"{vitima.Social.RelacaoCom(algoz.Assinatura)}, convivio {vitima.Social.Familiaridade(algoz.Assinatura)}");
			amigo.FuriaExtremaAte = 0;
			amigo.RaivaComumAte = 0;

			AoPerderALuta(vitima, algoz, morreu: true);
			Checa("amigo MORTO por um AMIGO dele: ninguem entra em furia",
				  Perfil(amigo).Raiva == NivelDeRaiva.Nenhuma, Perfil(amigo).Raiva.ToString());

			// ============================ 4. MORTO POR UM INIMIGO: O LUTO ============================
			VerboRelacao(vitima, $"{algoz.Assinatura}|neutro");   // o algoz deixa de ser amigo da vitima
			AoPerderALuta(vitima, algoz, morreu: true);

			Checa("amigo MORTO por um INIMIGO, na sua frente: FURIA EXTREMA",
				  Perfil(amigo).Raiva == NivelDeRaiva.Extrema, Perfil(amigo).Raiva.ToString());
			Checa("...e quem estava a 40 tiles nao viu nada (o 'na frente dele' e uma condicao)",
				  Perfil(longe).Raiva == NivelDeRaiva.Nenhuma, Perfil(longe).Raiva.ToString());
			Checa("...e quem MATOU nao se enfurece com o proprio crime",
				  Perfil(algoz).Raiva == NivelDeRaiva.Nenhuma, Perfil(algoz).Raiva.ToString());

			// ============================ 5. O NOCAUTE VALE O GRAU DE BAIXO ============================
			// Os DOIS graus tem chamador, e nao so a morte -- e desde 2026-10-11 o nocaute abre as mesmas
			// formas que a morte, por um preco de BP maior (secao 6). Ligar so a morte deixaria metade da
			// regra do dono sem dono.
			amigo.FuriaExtremaAte = 0;
			amigo.RaivaComumAte = 0;
			AoPerderALuta(vitima, algoz, morreu: false);
			Checa("amigo NOCAUTEADO por um inimigo: raiva COMUM (e nao a extrema)",
				  Perfil(amigo).Raiva == NivelDeRaiva.Comum, Perfil(amigo).Raiva.ToString());

			// ============================ 6. E ENTAO A TECLA C ============================
			// O elo final, e o unico que interessa ao jogador. Pelo `Transformar` -- a MESMA funcao
			// que a tecla C chama --, e nao pelo `Avaliar`: o jogador nao escolhe forma, ele pede pra
			// subir e o servidor decide o degrau. Perguntar so ao `Avaliar` deixaria de fora o unico
			// funil por onde a forma pode vazar em jogo.
			amigo.Forma.Entrar(Catalogo.IdBase);
			amigo.Forma.Liberadas.Clear();
			amigo.Forma.EstreiaVista.Clear();
			amigo.Ficha.Ki = amigo.Ficha.MaxKi;
			AplicarForma(amigo);

			// SO COM A RAIVA COMUM NO CORPO (um amigo caiu), O PODER DA PORTA NAO BASTA. Desde 2026-10-11 a
			// raiva comum abre o tronco Saiyajin tambem -- o dono: *"o rage normal tb faz vc se transformar da
			// mesma forma q o extreme, porem o requisito de bp ... e maior"* --, e o preco dela e a porta
			// vezes o agio (`FormaDef.AgioNaRaivaComum`, o `if("Angry")` do `supersaiyan.dm:112`).
			//
			// O CORPO FICA **ENTRE** OS DOIS PRECOS de proposito: e a faixa em que o nocaute nao move e a
			// morte move -- o mesmo corpo, o mesmo poder, duas dores. Ate ali esta checagem dizia "o
			// Saiyajin comum continua na base" com qualquer poder, porque o nocaute nao abria o tronco.
			double portaDoAmigo = PortaDeTeste(amigo, "ssj1");
			amigo.Ficha.BP = portaDoAmigo * (1 + Catalogo.AgioComumDoSaiyajin) / 2;
			amigo.Ficha.Statify();
			amigo.Ficha.Ki = amigo.Ficha.MaxKi;
			RecusaForma soComAQueda = amigo.Forma.Avaliar("ssj1", amigo.Ficha.BP, 1, false, Perfil(amigo));
			Transformar(amigo, subir: true);
			PassarACena(amigo);
			Checa("com o amigo so NOCAUTEADO e o poder ENTRE os dois precos, o Saiyajin comum continua na base (a raiva comum cobra mais poder)",
				  amigo.Forma.NaBase && soComAQueda == RecusaForma.SemPoderPraRaivaComum,
				  $"{soComAQueda}; chegou em {amigo.Forma.Atual}");

			// e com o LUTO ele sobe -- este e o SSJ1 alcancavel pelo caminho do jogador
			AoPerderALuta(vitima, algoz, morreu: true);
			amigo.Ficha.Ki = amigo.Ficha.MaxKi;
			Checa("com o amigo MORTO, o SSJ1 passa a ser alcancavel",
				  amigo.Forma.Avaliar("ssj1", amigo.Ficha.BP, 1, false, Perfil(amigo))
					  == RecusaForma.Pode,
				  amigo.Forma.Avaliar("ssj1", amigo.Ficha.BP, 1, false, Perfil(amigo)).ToString());

			Transformar(amigo, subir: true);
			PassarACena(amigo);
			Checa("**A TECLA C SAI DA BASE** -- o SSJ1 e alcancavel sem admin, por quem DECLAROU o amigo",
				  !amigo.Forma.NaBase, "continuou na base");
			Checa("...e o degrau que saiu e do tronco Saiyajin",
				  amigo.Forma.Def?.Linha == LinhaDeForma.Saiyajin,
				  $"{amigo.Forma.Atual} ({amigo.Forma.Def?.Linha})");
			Checa("...e o corpo recebeu o multiplicador da forma",
				  amigo.Ficha.ssjBuff > 1.5, $"ssjBuff {amigo.Ficha.ssjBuff:0.###}");

			// ============================ 6b. O AVESSO -- "DESTRAVADO" SO QUER DIZER ALGO CONTRA UM TRANCADO ============================
			// O passo 6 provou que o SSJ1 CHEGA. Ele nao prova o que o dono pediu, e a diferenca nao e
			// detalhe: **"esta destravado" passaria igualzinho num mundo onde tudo esta destravado**.
			// Uma bancada que so olha o lado positivo nao distingue "a corrente funciona" de "o gate
			// sumiu" -- e o gate sumindo e o modo de falha mais provavel deste sistema, porque
			// `Nenhuma` e o padrao de todo `PerfilDeFormas` e uma linha `Raiva:` esquecida no
			// construtor destravaria o jogo inteiro em silencio.
			//
			// ENTAO SAO OS TRES ESTADOS, NO MESMO CORPO E NA MESMA ORDEM DO JOGO:
			//   em paz -> nao sobe   ->   luto -> sobe   ->   passado o luto -> nao sobe de novo
			//
			// O MESMO CORPO nas tres e o que fecha a ultima brecha: com corpos diferentes, o "nao
			// sobe" poderia ser um corpo sem Ki, sem BP ou com a tecla C quebrada, e a bancada leria
			// isso como gate funcionando. Aqui o corpo que nao sobe e literalmente o que subiu.
			// ==========================================================================================================================
			algoz.Forma.Entrar(Catalogo.IdBase);
			algoz.Forma.Liberadas.Clear();
			algoz.Forma.EstreiaVista.Clear();
			algoz.FuriaExtremaAte = 0;
			algoz.RaivaComumAte = 0;
			algoz.Ficha.Ki = algoz.Ficha.MaxKi;
			AplicarForma(algoz);

			Checa("(avesso) quem nao viu ninguem cair esta em paz",
				  Perfil(algoz).Raiva == NivelDeRaiva.Nenhuma, Perfil(algoz).Raiva.ToString());

			// A RECUSA TEM QUE SER **POR FURIA**, e nao qualquer recusa. Sem esta linha, um corpo sem
			// Ki ou sem BP daria o mesmo verde e a bancada estaria medindo o gate errado -- que e
			// exatamente como um gate quebrado se esconde atras de outro que funciona.
			Checa("(avesso) e o SSJ1 e recusado POR FALTA DE FURIA (nao por BP, nao por Ki)",
				  algoz.Forma.Avaliar("ssj1", algoz.Ficha.BP, 1, false, Perfil(algoz))
					  == RecusaForma.SemFuria,
				  algoz.Forma.Avaliar("ssj1", algoz.Ficha.BP, 1, false, Perfil(algoz)).ToString());

			// E O MUNDO NAO ESTA DESTRAVADO: nenhuma forma concedida. `Liberadas` e o conjunto das
			// formas que alguem DEU (Mistico, Beast, Oozaru, ssj4 -- os quatro chamadores de
			// `Forma.Liberar`); se a bancada rodasse num personagem de admin com meia escada
			// liberada, o passo 6 daria verde sem a raiva ter feito nada.
			Checa("(avesso) e nada foi concedido a este corpo (o mundo nao esta aberto)",
				  algoz.Forma.Liberadas.Count == 0, $"{algoz.Forma.Liberadas.Count} liberada(s)");

			SubirAteParar(algoz);
			Checa("(avesso) **a tecla C nao sai da base** sem ninguem ter caido",
				  algoz.Forma.NaBase, "chegou em " + algoz.Forma.Atual);

			// AGORA O MESMO CORPO DECLARA A VITIMA AMIGA E A VE MORRER. Quem mata e o `longe`, de quem a
			// vitima nao declarou nada -- e portanto e inimigo pelo `Death.dm:75`.
			VerboRelacao(algoz, $"{vitima.Assinatura}|amigo");
			Checa("(avesso) o mesmo corpo declara a vitima amiga (o convivio dos dois paga)",
				  algoz.Social.EhAmigo(vitima.Assinatura),
				  $"{algoz.Social.RelacaoCom(vitima.Assinatura)}, convivio {algoz.Social.Familiaridade(vitima.Assinatura)}");
			AoPerderALuta(vitima, longe, morreu: true);
			algoz.Ficha.Ki = algoz.Ficha.MaxKi;

			Checa("(avesso) visto o amigo morrer, o MESMO corpo passa a poder",
				  algoz.Forma.Avaliar("ssj1", algoz.Ficha.BP, 1, false, Perfil(algoz))
					  == RecusaForma.Pode,
				  algoz.Forma.Avaliar("ssj1", algoz.Ficha.BP, 1, false, Perfil(algoz)).ToString());
			SubirAteParar(algoz);
			Checa("(avesso) ...e a MESMA tecla C sai da base",
				  !algoz.Forma.NaBase && algoz.Forma.Def?.Linha == LinhaDeForma.Saiyajin,
				  $"{algoz.Forma.Atual} ({algoz.Forma.Def?.Linha})");

			// ============================ E A PORTA **NAO** FECHA -- E ISSO E A REGRA ============================
			// Escrevi esta checagem ao contrario primeiro ("passada a janela, o C volta a nao sair da
			// base") e ela reprovou. O jogo esta certo e eu estava errado: `EstadoDeForma.Entrar`
			// chama `Liberar`, e o gate da raiva e `!Despertou(d.Id) && perfil.Raiva < pedeRaiva`
			// (`EstadoDeForma.cs:314`). **A raiva paga a entrada UMA VEZ**, e o motivo esta escrito
			// no proprio gate: `supersaiyan.dm:163-170` escreve `hasbeast = 1` e ENTREGA O VERB.
			// Cobrar de novo faria do SSJ1 um consumivel que so volta com um segundo amigo morto.
			//
			// A checagem fica -- virada pro lado certo. Ela agora PRENDE a regra: se alguem um dia
			// passar a cobrar a raiva toda vez, isto reprova e obriga a conversa em vez de deixar o
			// jogador descobrir sozinho que perdeu a forma.
			//
			// E O CONTROLE E O `longe`: mesmo instante, mesma raca, mesmo BP, mesma classe, mesmo
			// mundo -- e ele nunca despertou nada. Sem ele, o "Pode" do algoz poderia estar vindo de
			// o mundo ter aberto (a janela nao ter fechado de verdade, um gate esquecido, um perfil
			// devolvendo `Extrema` fixo) e ninguem saberia. E o par que responde "destravado em
			// relacao a que?".
			// ==================================================================================================
			algoz.FuriaExtremaAte -= (long)(SegundosDeRaiva * 1000) + 500;
			algoz.RaivaComumAte -= (long)(SegundosDeRaiva * 1000) + 500;
			algoz.Forma.Entrar(Catalogo.IdBase);
			algoz.Ficha.Ki = algoz.Ficha.MaxKi;
			AplicarForma(algoz);

			Checa("(avesso) passada a janela, a raiva dele acabou mesmo",
				  Perfil(algoz).Raiva == NivelDeRaiva.Nenhuma, Perfil(algoz).Raiva.ToString());
			Checa("(avesso) e o SSJ1 continua alcancavel -- **a raiva paga a entrada UMA VEZ**",
				  algoz.Forma.Despertou("ssj1")
				  && algoz.Forma.Avaliar("ssj1", algoz.Ficha.BP, 1, false, Perfil(algoz))
					  == RecusaForma.Pode,
				  algoz.Forma.Avaliar("ssj1", algoz.Ficha.BP, 1, false, Perfil(algoz)).ToString());
			SubirAteParar(algoz);
			Checa("(avesso) ...e a tecla C sobe sem raiva nenhuma, porque ele JA despertou",
				  !algoz.Forma.NaBase, "ficou na base");

			// O CONTROLE, NO MESMO INSTANTE: quem nunca despertou continua trancado.
			//
			// O BP DELE SOBE ACIMA DA PORTA PESSOAL primeiro, e isso nao e conveniencia: `ssjat` e
			// SORTEADO por personagem, e um sorteio alto faria o `Avaliar` responder `SemPoder` em vez
			// de `SemFuria`. A bancada daria vermelho falando de raiva por causa de BP -- e uma bancada
			// que reprova pelo motivo errado ensina a desconfiar dela, nao do jogo.
			longe.FuriaExtremaAte = 0;
			longe.RaivaComumAte = 0;
			longe.Ficha.BP = Math.Max(longe.Ficha.BP, PortaDeTeste(longe, "ssj1") * 1.5);
			longe.Ficha.Statify();
			longe.Forma.Entrar(Catalogo.IdBase);
			longe.Ficha.Ki = longe.Ficha.MaxKi;
			AplicarForma(longe);
			Checa("(avesso) **e o vizinho que nunca despertou continua trancado, no mesmo mundo**",
				  !longe.Forma.Despertou("ssj1")
				  && longe.Forma.Avaliar("ssj1", longe.Ficha.BP, 1, false, Perfil(longe))
					  == RecusaForma.SemFuria,
				  longe.Forma.Avaliar("ssj1", longe.Ficha.BP, 1, false, Perfil(longe)).ToString());
			SubirAteParar(longe);
			Checa("(avesso) ...e a tecla C dele nao sai da base",
				  longe.Forma.NaBase, "chegou em " + longe.Forma.Atual);

			// ============================ 6c. O WRATHFUL, COM UM NOCAUTE E SEM MORTE NENHUMA ============================
			// Esta secao nasceu da regra anterior do dono (*"o Legendary Saiyajin tem a skill `legendary
			// anger` e por isso precisa de MENOS: basta ver um amigo ser NOCAUTEADO"*), em que a raiva comum
			// era desconto so da linha Legendary. Em 2026-10-11 ele pediu a regra do DM: a raiva comum abre
			// as formas de raiva de TODO MUNDO, por um preco de BP maior -- e la o Legendary paga 1,5x a
			// porta (`supersaiyan.dm:68`), contra 1,2x do Saiyajin comum (`:112`).
			//
			// O QUE ESTA SECAO MEDE, pela corrente inteira ATE O NOME DA FORMA:
			//   1. que basta um NOCAUTE -- nao ha morte nenhuma aqui, e a bancada confere que a
			//      janela do LUTO ficou fechada enquanto a do nocaute abria;
			//   2. que com o poder da porta o nocaute NAO abre o `wrathful`, e com 1,5x ele abre PELO NOME;
			//   3. que o MESMO nocaute, no MESMO instante, nao move o Saiyajin comum ao lado enquanto ele
			//      nao tiver o poder que a raiva comum cobra DELE -- e move quando tiver.
			//
			// E O "AMIGO" DESTA REGRA E O DECLARADO: quem disse `amigo` (e nao so `melhor amigo`/`amor`)
			// ja se enfurece com a queda -- e a metade `is_friend` de `KO.dm:36`, que era a dos 50 pontos.
			// ==========================================================================================================
			Checa("a MENOR raiva que abre o `wrathful` e a comum (a do nocaute), e nao a do luto",
				  Catalogo.RaivaExigida(Catalogo.Def(Catalogo.IdWrathful)) == NivelDeRaiva.Comum,
				  Catalogo.RaivaExigida(Catalogo.Def(Catalogo.IdWrathful)).ToString());

			// O BP SAI DO LIMIAR PESSOAL, e nao de um numero escrito aqui: `restssjat` e sorteado por
			// personagem (`LimiaresPessoais.RolarSaiyajin`), entao um `20_000_000` cravado funcionaria
			// em quase todo mundo e reprovaria calado no sorteio ruim. O poder fica logo acima do PRECO DA
			// RAIVA COMUM do `wrathful` (a porta vezes o agio do Legendary) e abaixo da porta do degrau
			// seguinte: deixa **um** degrau alcancavel -- e e isso que torna o nome da forma uma medida em
			// vez de um palpite sobre o que o `Proxima` escolhe. (Era a media geometrica das duas portas,
			// que no sorteio mais apertado cai exatamente em 1,5x a do `wrathful`.)
			lenda.Ficha.Class = "Legendary";
			lenda.Ficha.Statify();
			double portaWrathful = PortaDeTeste(lenda, Catalogo.IdWrathful);
			double portaSeguinte = PortaDeTeste(lenda, "c_type");
			lenda.Ficha.BP = portaWrathful * Catalogo.AgioComumDoLegendary * 1.05;
			lenda.Ficha.Statify();
			lenda.Ficha.Ki = lenda.Ficha.MaxKi;
			lenda.Forma.Entrar(Catalogo.IdBase);
			lenda.Forma.Liberadas.Clear();
			lenda.Forma.EstreiaVista.Clear();
			lenda.FuriaExtremaAte = 0;
			lenda.RaivaComumAte = 0;
			AplicarForma(lenda);

			Checa("o corpo lendario passa da porta do `wrathful` e nao da do degrau seguinte",
				  lenda.Ficha.BP > portaWrathful && lenda.Ficha.BP < portaSeguinte,
				  $"BP {lenda.Ficha.BP:0} entre {portaWrathful:0} e {portaSeguinte:0}");
			Checa("em paz, o `wrathful` e recusado POR FALTA DE FURIA",
				  lenda.Forma.Avaliar(Catalogo.IdWrathful, lenda.Ficha.BP, 1, false, Perfil(lenda))
					  == RecusaForma.SemFuria,
				  lenda.Forma.Avaliar(Catalogo.IdWrathful, lenda.Ficha.BP, 1, false, Perfil(lenda))
					  .ToString());
			SubirAteParar(lenda);
			Checa("...e a tecla C nao sai da base",
				  lenda.Forma.NaBase, "chegou em " + lenda.Forma.Atual);

			// UM NOCAUTE, e so. `morreu: false` -- e a checagem logo abaixo confere que nenhuma janela
			// de LUTO abriu, que e a metade "e nao com uma morte so" do enunciado.
			DeclararDeTeste(lenda, vitima, Relacao.Bom);

			// O SAIYAJIN COMUM DO CONTRASTE VOLTA A NAO TER DESPERTADO NADA -- e esta linha custou uma
			// rodada vermelha. Ele despertou o SSJ1 no passo 6, e forma despertada nao pergunta mais
			// pela raiva (`EstadoDeForma.cs:314`): ele subiria com o nocaute e a bancada leria isso
			// como "o desconto do Legendary nao existe". O que esta secao compara e o GATE, e quem ja
			// atravessou o gate nao tem gate pra medir.
			amigo.Forma.Entrar(Catalogo.IdBase);
			amigo.Forma.Liberadas.Clear();
			amigo.Forma.EstreiaVista.Clear();
			amigo.FuriaExtremaAte = 0;
			amigo.RaivaComumAte = 0;
			amigo.Ficha.Ki = amigo.Ficha.MaxKi;
			AplicarForma(amigo);

			AoPerderALuta(vitima, longe, morreu: false);
			lenda.Ficha.Ki = lenda.Ficha.MaxKi;

			Checa("o nocaute abre a janela COMUM e deixa a do LUTO fechada",
				  Perfil(lenda).Raiva == NivelDeRaiva.Comum && lenda.FuriaExtremaAte == 0,
				  $"{Perfil(lenda).Raiva}, luto ate {lenda.FuriaExtremaAte}");
			RecusaForma naPortaDoLendario = lenda.Forma.Avaliar(Catalogo.IdWrathful, portaWrathful * 1.25, 1, false, Perfil(lenda));
			Checa("com um nocaute so e o poder ENTRE a porta e o preco da raiva comum, o `wrathful` NAO abre (o Legendary nao tem mais desconto)",
				  naPortaDoLendario == RecusaForma.SemPoderPraRaivaComum, naPortaDoLendario.ToString());
			Checa("**com um nocaute so e o poder que a raiva comum cobra (1,5x a porta), o `wrathful` abre**",
				  lenda.Forma.Avaliar(Catalogo.IdWrathful, lenda.Ficha.BP, 1, false, Perfil(lenda))
					  == RecusaForma.Pode,
				  lenda.Forma.Avaliar(Catalogo.IdWrathful, lenda.Ficha.BP, 1, false, Perfil(lenda))
					  .ToString());

			SubirAteParar(lenda);
			Checa("...e a tecla C leva ao `wrathful` PELO NOME (e nao 'a algum degrau da linha')",
				  lenda.Forma.Atual == Catalogo.IdWrathful, "parou em " + lenda.Forma.Atual);
			Checa("...e o corpo recebeu o multiplicador da forma",
				  lenda.Ficha.ssjBuff > 1.4, $"ssjBuff {lenda.Ficha.ssjBuff:0.###}");

			// E O SAIYAJIN COMUM AO LADO, QUE VIU O MESMO NOCAUTE NO MESMO INSTANTE: ele continua na base
			// enquanto so tem o poder ENTRE os dois precos (o que a secao 6 deixou nele), e sobe quando
			// passa a ter o que a raiva comum cobra dele -- 1,2x a porta, MENOS que o 1,5x do Legendary.
			// Ate 2026-10-11 este contraste era o desconto do Legendary ("o comum nao sobe com nocaute").
			SubirAteParar(amigo);
			Checa("...enquanto o Saiyajin comum, que viu o MESMO nocaute, continua na base com o poder entre os dois precos",
				  amigo.Forma.NaBase, "chegou em " + amigo.Forma.Atual);
			amigo.Ficha.BP = PortaDeTeste(amigo, "ssj1") * Catalogo.AgioComumDoSaiyajin * 1.05;
			amigo.Ficha.Statify();
			amigo.Ficha.Ki = amigo.Ficha.MaxKi;
			SubirAteParar(amigo);
			Checa("**...e com o poder que a raiva comum cobra DELE (1,2x a porta), o MESMO nocaute o leva ao Super Saiyajin**",
				  amigo.Forma.Atual == "ssj1", "parou em " + amigo.Forma.Atual);

			// ============================ 7. O QUE ACONTECEU NAO MUDOU O QUE NINGUEM DECLAROU ============================
			// As secoes 1 a 6 mataram a `vitima` na frente do `amigo` varias vezes, pela mao do `algoz`. Na
			// afinidade isso bastava pro jogo decidir sozinho: o algoz virava INIMIGO e rival do amigo, a
			// declaracao de afeto por quem te matava caia, os pontos desciam. Nada disso existe mais -- e esta
			// secao e a medida: depois de tudo, cada ficha diz exatamente o que o dono dela disse.
			Checa("**o amigo viu o algoz matar a amiga dele varias vezes, e o jogo NAO declarou o algoz inimigo dele**",
				  amigo.Social.RelacaoCom(algoz.Assinatura) == Relacao.Nenhuma,
				  amigo.Social.RelacaoCom(algoz.Assinatura).ToString());
			Checa("...a vitima, morta por ele quatro vezes, continua com o 'neutro' que ELA declarou",
				  vitima.Social.RelacaoCom(algoz.Assinatura) == Relacao.Neutro,
				  vitima.Social.RelacaoCom(algoz.Assinatura).ToString());
			Checa("...e a declaracao do amigo pela vitima esta de pe",
				  amigo.Social.RelacaoCom(vitima.Assinatura) == Relacao.Bom, "");
			int vitimaAlgoz = vitima.Social.Familiaridade(algoz.Assinatura);
			Checa("**o que as mortes fizeram foi SOMAR convivio entre a vitima e o algoz, igual nos dois lados**",
				  vitimaAlgoz >= 50 + Convivio.FamiliaridadePorMorte
				  && vitimaAlgoz == algoz.Social.Familiaridade(vitima.Assinatura),
				  $"{vitimaAlgoz} / {algoz.Social.Familiaridade(vitima.Assinatura)}");

			// E O CASO QUE A REGRA ANTIGA DESFAZIA A FORCA: quem declarou AMIGO e foi morto por ele. A
			// declaracao e de quem declarou; se ele nao quiser mais, ele muda na aba.
			cobaiaA.Social.Conhecidos.Clear();
			cobaiaB.Social.Conhecidos.Clear();
			DeclararDeTeste(cobaiaA, cobaiaB, Relacao.Bom);
			AoPerderALuta(cobaiaA, cobaiaB, morreu: true);
			Checa("**quem declarou 'amigo' e foi MORTO por ele continua tendo declarado amigo** (o jogo nao desdiz ninguem)",
				  cobaiaA.Social.RelacaoCom(cobaiaB.Assinatura) == Relacao.Bom,
				  cobaiaA.Social.RelacaoCom(cobaiaB.Assinatura).ToString());
			Checa("...e o convivio dos dois SUBIU com a morte (20), nos dois lados",
				  cobaiaA.Social.Familiaridade(cobaiaB.Assinatura) == Convivio.FamiliaridadePorMorte
				  && cobaiaB.Social.Familiaridade(cobaiaA.Assinatura) == Convivio.FamiliaridadePorMorte,
				  $"{cobaiaA.Social.Familiaridade(cobaiaB.Assinatura)} / {cobaiaB.Social.Familiaridade(cobaiaA.Assinatura)}");

			// ============================ 8. A DECLARACAO E PAGA COM CONVIVIO ============================
			// O verb confere no servidor -- apagar o botao no cliente nunca foi permissao.
			Conhecido? ficha = amigo.Social.Ficha(algoz.Assinatura);
			if (ficha != null) ficha.Familiaridade = 99;
			VerboRelacao(amigo, $"{algoz.Assinatura}|melhor amigo");
			Checa("declarar 'melhor amigo' com 99 de convivio e RECUSADO (pede 100)",
				  amigo.Social.RelacaoCom(algoz.Assinatura) == Relacao.Nenhuma,
				  amigo.Social.RelacaoCom(algoz.Assinatura).ToString());

			if (ficha != null) ficha.Familiaridade = 100;
			VerboRelacao(amigo, $"{algoz.Assinatura}|melhor amigo");
			Checa("...e com 100 ela passa",
				  amigo.Social.RelacaoCom(algoz.Assinatura) == Relacao.MuitoBom,
				  amigo.Social.RelacaoCom(algoz.Assinatura).ToString());
			VerboRelacao(amigo, $"{algoz.Assinatura}|muito bom");
			VerboRelacao(amigo, $"{algoz.Assinatura}|amor");
			Checa("...o nome de ANTES ('muito bom') ainda e entendido, e 'amor' (200) continua recusado",
				  amigo.Social.RelacaoCom(algoz.Assinatura) == Relacao.MuitoBom, "");
			VerboRelacao(amigo, $"{algoz.Assinatura}|ligado");
			Checa("...um rotulo da afinidade antiga ('ligado') nao e declaracao nenhuma",
				  amigo.Social.RelacaoCom(algoz.Assinatura) == Relacao.MuitoBom, "");
			VerboRelacao(amigo, "0000000000|neutro");
			Checa("...e declarar pra uma assinatura que eu nao conheco nao cria ficha",
				  !amigo.Social.Conhece("0000000000"), "");

			// E A DECLARACAO DE RIVAL QUE SE RESPEITA: enfurece pela MORTE, e nao pela queda.
			var soRival = new Convivio();
			soRival.Fotografar("x", "x", "x", "x", 0, 0);
			soRival.Ficha("x")!.Relacao = Relacao.RivalBom;
			Checa("'rival amigo' enfurece por MORTE (a lista de `Death.dm:79`)", soRival.LutoPorMorte("x"), "");
			Checa("...mas nao por NOCAUTE (`KO.dm:36`), e apanhar dele e treino (`Death.dm:75`)",
				  !soRival.LutoPorQueda("x") && !soRival.AlgozEhInimigo("x"), "");

			// OS VERBS QUE SAIRAM COM A AFINIDADE nao sao mais deste canal (o funil segue e diz "desconhecido")
			Checa("o pedido de amizade e o `rival` nao existem mais como verbos do convivio",
				  !ComandoDeConvivio(amigo, "amizade_pedir", vitima.Id.ToString())
				  && !ComandoDeConvivio(amigo, "amizade_aceitar", "")
				  && !ComandoDeConvivio(amigo, "amizade_recusar", "")
				  && !ComandoDeConvivio(amigo, "rival", vitima.Id.ToString()), "");

			// ============================ 8b. CONVIVIO NA PORRADA (dono, 2026-09-05) ============================
			// "apanhar muito ... o convivio ainda era 0 e eu so conseguia declarar ele como neutro". Cair e
			// morrer pela mao de alguem rendem convivio nos DOIS lados; golpe que encosta rende com 10% de chance.
			{
				cobaiaA.Social.Conhecidos.Remove(cobaiaB.Assinatura);
				cobaiaB.Social.Conhecidos.Remove(cobaiaA.Assinatura);
				ConvivioPorDerrota(cobaiaA, cobaiaB, morreu: false);
				Checa("ser DERRUBADO por alguem rende 10 de convivio -- nos dois lados, mesmo entre quem nunca se viu",
					  cobaiaA.Social.Familiaridade(cobaiaB.Assinatura) == Convivio.FamiliaridadePorQueda
					  && cobaiaB.Social.Familiaridade(cobaiaA.Assinatura) == Convivio.FamiliaridadePorQueda,
					  $"{cobaiaA.Social.Familiaridade(cobaiaB.Assinatura)} / {cobaiaB.Social.Familiaridade(cobaiaA.Assinatura)}");
				ConvivioPorDerrota(cobaiaA, cobaiaB, morreu: true);
				Checa("...e MORRER pela mao dele rende 20 (30 no total)",
					  cobaiaA.Social.Familiaridade(cobaiaB.Assinatura) == Convivio.FamiliaridadePorQueda + Convivio.FamiliaridadePorMorte
					  && cobaiaB.Social.Familiaridade(cobaiaA.Assinatura) == Convivio.FamiliaridadePorQueda + Convivio.FamiliaridadePorMorte,
					  $"{cobaiaA.Social.Familiaridade(cobaiaB.Assinatura)}");
				int antesDosGolpes = cobaiaA.Social.Familiaridade(cobaiaB.Assinatura);
				for (int i = 0; i < 400; i++) ConvivioPorGolpe(cobaiaA, cobaiaB);
				int ganho = cobaiaA.Social.Familiaridade(cobaiaB.Assinatura) - antesDosGolpes;
				Checa("400 golpes que encostam rendem ~40 de convivio (10% por golpe), e o mesmo tanto pro outro lado",
					  ganho is >= 20 and <= 65 && cobaiaB.Social.Familiaridade(cobaiaA.Assinatura) - antesDosGolpes == ganho, $"{ganho}");
				Checa("com tres mortes (60) o 'inimigo mortal' (50) ja pode ser declarado -- e o 'amor' (200) continua sendo tempo",
					  Convivio.FamiliaridadeExigida(Relacao.Odio) <= 3 * Convivio.FamiliaridadePorMorte
					  && Convivio.FamiliaridadeExigida(Relacao.Amor) > 8 * Convivio.FamiliaridadePorQueda, "");
				Checa("CONTRA-EXEMPLO: bater em si mesmo nao rende convivio nenhum",
					  Verificando(() => ConvivioPorGolpe(cobaiaA, cobaiaA), () => cobaiaA.Social.Familiaridade(cobaiaA.Assinatura) == 0), "");

				// A CONVERSA: 1% por fala ouvida, nos dois sentidos. Duas mil falas ~ 20 pontos.
				cobaiaA.Social.Conhecidos.Remove(cobaiaB.Assinatura);
				cobaiaB.Social.Conhecidos.Remove(cobaiaA.Assinatura);
				for (int i = 0; i < 2000; i++) Ouviu(cobaiaA, cobaiaB);
				int pelaFala = cobaiaA.Social.Familiaridade(cobaiaB.Assinatura);
				Checa("2000 falas ouvidas rendem ~20 de convivio (1% por fala), e o mesmo tanto pro outro lado",
					  pelaFala is >= 5 and <= 45 && cobaiaB.Social.Familiaridade(cobaiaA.Assinatura) == pelaFala, $"{pelaFala}");
			}

			// ============================ 8c. OS DETALHES DE UM CONHECIDO: SO PRA QUEM CONVIVEU ============================
			OsDetalhesAoVivo(cobaiaA, cobaiaB, avisos, Checa);

			// ============================ 9. ISSO TUDO SOBREVIVE AO LOGOUT, E O SAVE DE ANTES CARREGA ============================
			ASobrevivenciaNoDisco(amigo, vitima, algoz, avisos, Checa);

			// ============================ 10. E CADA FAMILIA COM O DEFEITO INJETADO ============================
			AsMutacoesDoConvivio(amigo, vitima, cobaiaA, cobaiaB, Checa);
		}
		finally
		{
			Convivio.AmigoSoPeloConvivioDeTeste = false;
			Convivio.TempoJuntoNaoContaDeTeste = false;
			Convivio.DeclararSemConvivioDeTeste = false;
			Convivio.DetalhesSemConvivioDeTeste = false;
			EscutaDeAvisos = escutaDeFora;
			foreach (ServerPlayer p in new[] { amigo, vitima, algoz, longe, lenda, cobaiaA, cobaiaB })
			{
				_players.Remove(p.Id);
				ZoneList(zona.Hash).Remove(p);
			}
		}
	}

	/// <summary>
	/// ============================ AS FAMILIAS DO CONVIVIO, CADA UMA COM O DEFEITO INJETADO ============================
	/// As secoes 1-9 afirmam. Esta pergunta se elas SABEM FICAR VERMELHAS -- e ela usa o
	/// <see cref="Mutacao"/> da bancada da prova, que e a receita da casa: **mede, estraga, mede,
	/// conserta, mede**. Tres linhas por familia, e a do meio e a unica que interessa.
	///
	/// OS CORPOS DA MUTACAO SAO OUTROS (`cobaiaA`/`cobaiaB`, a 100 tiles) nas familias que mexem em
	/// convivio e matam pra medir: fazer isso com o par das secoes 1-9 apagaria o estado que elas
	/// construiram.
	/// ==================================================================================================================
	/// </summary>
	private void AsMutacoesDoConvivio(ServerPlayer amigo, ServerPlayer vitima,
									  ServerPlayer cobaiaA, ServerPlayer cobaiaB,
									  Action<string, bool, string> Checa)
	{
		Checagem Mutar = (n, c, d) => Checa(n, c, d);

		void Zerar()
		{
			cobaiaA.Social.Conhecidos.Clear();
			cobaiaB.Social.Conhecidos.Clear();
			cobaiaA.FuriaExtremaAte = cobaiaA.RaivaComumAte = 0;
			cobaiaB.FuriaExtremaAte = cobaiaB.RaivaComumAte = 0;
		}

		void QuinhentosPassos()
		{
			// SO OS DOIS ANDAM: o prazo (`ProximaAproximacao`) e por pessoa, e a bancada roda dentro
			// de um quadro so -- os outros corpos ficam presos na propria cadencia de 3 s.
			for (int i = 0; i < 500; i++)
			{
				cobaiaA.ProximaAproximacao = 0;
				cobaiaB.ProximaAproximacao = 0;
				TickDoConvivio();
			}
		}

		// =====================================================================
		// (1) 25 MINUTOS LADO A LADO PAGAM O "AMIGO" -- e o defeito e a DISTANCIA
		// =====================================================================
		// O criterio atravessa o `TickDoConvivio` de verdade, 500 vezes, e termina no VERB. O defeito
		// injetado nao e um numero trocado: e o par 40 tiles separado, fora do `FRIEND_RANGE` de 6. Sem
		// ele, "500 passos pagam o amigo" ficaria verde numa versao que rendesse convivio com quem esta em
		// outro continente -- o oposto do que o dono pediu (*"ficou BASTANTE TEMPO COM VC"*).
		Vec2 ondeEle = cobaiaB.Pos;

		bool VinteCincoMinutosPagamOAmigo()
		{
			Zerar();
			QuinhentosPassos();
			VerboRelacao(cobaiaA, $"{cobaiaB.Assinatura}|amigo");
			VerboRelacao(cobaiaB, $"{cobaiaA.Assinatura}|amigo");
			return cobaiaA.Social.EhAmigo(cobaiaB.Assinatura) && cobaiaB.Social.EhAmigo(cobaiaA.Assinatura);
		}

		Mutacao(Mutar,
			$"**{Convivio.MinutosJuntosPara(Relacao.Bom):0} MINUTOS lado a lado pagam a declaracao de amigo**, pelo tique e pelo verb de verdade",
			"os dois passam a viver a 40 tiles um do outro (fora do `FRIEND_RANGE` de 6)",
			VinteCincoMinutosPagamOAmigo,
			() => cobaiaB.Pos = cobaiaA.Pos + new Vec2(40 * ZoneCollision.TileSize, 0),
			() => cobaiaB.Pos = ondeEle);

		// E o mesmo criterio com o OUTRO defeito possivel desta mudanca: a afinidade sair e o tempo junto
		// nao ir pra lugar nenhum (o convivio so subiria por fala e porrada).
		Mutacao(Mutar,
			"**o tempo junto rende convivio** (a taxa que era da afinidade)",
			"o passo de proximidade nao rende nada (`Convivio.TempoJuntoNaoContaDeTeste`)",
			VinteCincoMinutosPagamOAmigo,
			() => Convivio.TempoJuntoNaoContaDeTeste = true,
			() => Convivio.TempoJuntoNaoContaDeTeste = false);

		// =====================================================================
		// (2) SO A DECLARACAO ENFURECE -- e o defeito e a AFINIDADE DE VOLTA
		// =====================================================================
		// O pedido do dono, medido pelo funil: os dois conviveram os 25 minutos, NINGUEM declarou nada, e a
		// morte de um nao enfurece o outro. O defeito injetado e o jogo voltar a concluir "amigo" pelo
		// convivio -- exatamente o que a afinidade fazia aos 50 pontos.
		//
		// QUEM MATA E UM TERCEIRO QUE NUNCA CONVIVEU COM NINGUEM (a 60 tiles das cobaias): com o defeito
		// ligado, um algoz que tivesse convivido com a vitima viraria "amigo" dela tambem, a morte viraria
		// "duelo entre amigos" e o criterio continuaria verde pelo motivo errado. Onde o algoz esta nao
		// importa pro luto -- so quem ASSISTE precisa estar perto.
		var terceiro = new ServerPlayer
		{
			Id = IdBaseDoConvivioDeTeste + 8, Peer = null, Name = "bancada: o terceiro", Race = "Saiyan",
			Genero = "Male", Idade = 25, Zone = cobaiaA.Zone, Pos = cobaiaA.Pos + new Vec2(60 * ZoneCollision.TileSize, 0),
			Conta = "bancada_convivio_8", Slot = 0,
			Ficha = new Jandirus.Core.Stats.Fighter { Race = "Saiyan", BP = 3_000_000 },
		};
		terceiro.Ficha.Class = "Normal";
		PorNoMundo(terceiro);
		try
		{
			bool SemDeclararNinguemSeEnfurece()
			{
				Zerar();
				QuinhentosPassos();
				AoPerderALuta(cobaiaB, terceiro, morreu: true);
				return cobaiaA.Social.Familiaridade(cobaiaB.Assinatura) >= Convivio.FamiliaridadeExigida(Relacao.Bom)
					&& Perfil(cobaiaA).Raiva == NivelDeRaiva.Nenhuma;
			}

			Mutacao(Mutar,
				"**sem declaracao, o convivio sozinho nao enfurece ninguem** (25 min juntos, e a morte dele nao te move)",
				"o jogo volta a concluir 'amigo' pelo convivio (`Convivio.AmigoSoPeloConvivioDeTeste` -- a afinidade de volta)",
				SemDeclararNinguemSeEnfurece,
				() => Convivio.AmigoSoPeloConvivioDeTeste = true,
				() => Convivio.AmigoSoPeloConvivioDeTeste = false);

			// =====================================================================
			// (3) O SPAR NAO ENFURECE A PLATEIA -- e o defeito e a vitima desdizer
			// =====================================================================
			// "O nocaute nao enfureceu ninguem" e a checagem mais facil do mundo de deixar verde por engano:
			// basta o nocaute nao enfurecer nunca. O defeito injetado tira a declaracao da VITIMA pelo
			// algoz, e ai o `AlgozEhInimigo` (`Death.dm:75`) responde "isto nao era treino" -- e o mesmo
			// nocaute passa a enfurecer quem assiste.
			bool aVitimaChamaOAlgozDeAmigo = true;

			bool OSparNaoEnfurece()
			{
				Zerar();
				terceiro.Social.Conhecidos.Clear();
				DeclararDeTeste(cobaiaA, cobaiaB, Relacao.MuitoBom);                       // quem assiste gosta da vitima
				if (aVitimaChamaOAlgozDeAmigo) DeclararDeTeste(cobaiaB, terceiro, Relacao.Bom);   // e a vitima, do algoz
				AoPerderALuta(cobaiaB, terceiro, morreu: false);
				return Perfil(cobaiaA).Raiva == NivelDeRaiva.Nenhuma;
			}

			Mutacao(Mutar,
				"**o nocaute entre AMIGOS (o spar) nao enfurece quem assiste** -- treinar com o amigo nao e fabrica de furia",
				"a vitima nao declarou o algoz amigo: o `AlgozEhInimigo` passa a dizer que aquilo era briga de verdade",
				OSparNaoEnfurece,
				() => aVitimaChamaOAlgozDeAmigo = false,
				() => aVitimaChamaOAlgozDeAmigo = true);
		}
		finally
		{
			_players.Remove(terceiro.Id);
			ZoneList(terceiro.Zone.Hash).Remove(terceiro);
		}

		// =====================================================================
		// (4) CAIR PELA MAO DE ALGUEM RENDE CONVIVIO NOS DOIS LADOS -- e o defeito e o `EhPessoa`
		// =====================================================================
		// O defeito injetado e o corte que o dono perguntou por escrito ("e o cidadao?"): o algoz vira
		// um corpo do mundo (`Papel` preenchido) e o funil para de contar. Ele prova as duas pontas de
		// uma vez -- que a familia mede o FUNIL (`AoPerderALuta`), e que NPC realmente nao entra nisto. Se
		// ficasse verde com o `Papel` preenchido, daria pra pagar "amigo" apanhando de cidadao.
		var papelDeMentira = new Jandirus.Core.Npc.PapelDeNpc(
			new Jandirus.Core.Npc.MoldeDeNpc { Id = "bancada", Nome = "cobaia" }, 0);

		bool AMorteSomaNosDoisLados()
		{
			Zerar();
			AoPerderALuta(cobaiaA, cobaiaB, morreu: true);
			return cobaiaA.Social.Familiaridade(cobaiaB.Assinatura) == Convivio.FamiliaridadePorMorte
				&& cobaiaB.Social.Familiaridade(cobaiaA.Assinatura) == Convivio.FamiliaridadePorMorte;
		}

		Mutacao(Mutar,
			$"**morrer pela mao de alguem soma {Convivio.FamiliaridadePorMorte} de convivio nos DOIS lados**, pelo funil `AoPerderALuta`",
			"o algoz vira um NPC do mundo (`Papel` preenchido) -- o corte do `EhPessoa`",
			AMorteSomaNosDoisLados,
			() => cobaiaB.Papel = papelDeMentira,
			() => cobaiaB.Papel = null);

		// =====================================================================
		// (5) O PORTAO DO VERB -- e o defeito e declarar de graca
		// =====================================================================
		// Um pacote na mao (`relacao <assinatura>|amor`) de quem acabou de conhecer alguem. Sem o portao, a
		// furia -- a porta do SSJ1 -- custaria um clique em quem apareceu ha tres segundos.
		bool AmorNaoSeDeclaraPraQuemAcabouDeAparecer()
		{
			Zerar();
			Fotografar(cobaiaA, cobaiaB, 0);
			VerboRelacao(cobaiaA, $"{cobaiaB.Assinatura}|amor");
			VerboRelacao(cobaiaA, $"{cobaiaB.Assinatura}|amigo");
			return cobaiaA.Social.RelacaoCom(cobaiaB.Assinatura) == Relacao.Nenhuma
				&& !cobaiaA.Social.LutoPorMorte(cobaiaB.Assinatura);
		}

		Mutacao(Mutar,
			"**com convivio zero o verb recusa 'amor' e 'amigo'** (apagar o botao no cliente nunca foi permissao)",
			"declarar nao cobra convivio (`Convivio.DeclararSemConvivioDeTeste`)",
			AmorNaoSeDeclaraPraQuemAcabouDeAparecer,
			() => Convivio.DeclararSemConvivioDeTeste = true,
			() => Convivio.DeclararSemConvivioDeTeste = false);

		// =====================================================================
		// (6) A DECLARACAO ATRAVESSA O DISCO -- e o defeito e ela nao ir
		// =====================================================================
		// A secao 9 grava e le pelo `AccountStore` de verdade e diz "a declaracao voltou". Falta saber se
		// ela LE A FICHA ou se ela sempre diz que sim: o defeito apaga a declaracao antes de gravar.
		Conhecido fichaDaVitima = amigo.Social.Ficha(vitima.Assinatura)!;
		Relacao declarada = fichaDaVitima.Relacao;

		bool OAmigoVoltaDoDisco()
		{
			string pasta = Path.Combine(Path.GetTempPath(),
										"jandirus_mutacao_" + Guid.NewGuid().ToString("N"));
			try
			{
				var loja = new AccountStore(pasta);
				var conta = new AccountSave { Conta = "bancada_mutacao" };
				conta.Slots[0] = AccountStore.DeJogador(amigo, 0);
				loja.Gravar(conta);

				Convivio? meu = loja.Carregar("bancada_mutacao")?.Slots[0]?.Social;
				return meu != null && meu.EhAmigo(vitima.Assinatura) && meu.LutoPorMorte(vitima.Assinatura);
			}
			catch (Exception e) { GD.Print($"[bancada] mutacao do disco: {e.Message}"); return false; }
			finally
			{
				try { if (Directory.Exists(pasta)) Directory.Delete(pasta, recursive: true); }
				catch (Exception e) { GD.Print($"[bancada] nao consegui apagar {pasta}: {e.Message}"); }
			}
		}

		Mutacao(Mutar,
			"**a declaracao atravessa o disco** (quem eu chamei de amigo continua amigo depois do save, e e isso que a furia le)",
			"a declaracao e apagada antes de gravar",
			OAmigoVoltaDoDisco,
			() => fichaDaVitima.Relacao = Relacao.Nenhuma,
			() => fichaDaVitima.Relacao = declarada);

		Checa("...e depois de oito defeitos injetados e desfeitos (sete aqui, um na secao dos detalhes), os quatro "
			  + "botoes de defeito estao desligados e a declaracao medida nas secoes 1-9 esta como estava",
			  !Convivio.AmigoSoPeloConvivioDeTeste && !Convivio.TempoJuntoNaoContaDeTeste
			  && !Convivio.DeclararSemConvivioDeTeste && !Convivio.DetalhesSemConvivioDeTeste
			  && fichaDaVitima.Relacao == declarada
			  && cobaiaB.Papel == null && cobaiaB.Pos.Equals(ondeEle),
			  $"declaracao={fichaDaVitima.Relacao}");
	}

	/// <summary>
	/// ============================ OS DETALHES DE UM CONHECIDO, PELOS BYTES QUE SAEM ============================
	/// O segundo pedido do dono (2026-10-10): *"clicar em pessoas q vc conhece (tem q ter um certo nivel de
	/// convivio minimo) vc pode ver mais detalhes da pessoa, como idade, a backstory dela"*.
	///
	/// A PERGUNTA mora no Core (`Convivio.PodeVerDetalhes`, medida na bancada `convivio`). O que so daqui se ve e
	/// O QUE O SERVIDOR MANDA: a <see cref="EscutaDeDetalhes"/> guarda os bytes do pacote no ponto em que ele vai
	/// pro fio, e a bancada confere tres coisas que "a tela esconde" nunca garantiria:
	///
	///   * abaixo do convivio minimo o pacote NEM NASCE (e a historia nem e copiada pra ficha de quem pede);
	///   * quando nasce, ele acaba exatamente no ultimo campo publico (o leitor do cliente o consome inteiro) e
	///     os valores sigilosos do corpo -- poder, classe, conta, lugar -- nao estao em byte nenhum dele;
	///   * o que sai e a FOTOGRAFIA (o que eu vi da ultima vez), e nao o corpo vivo da pessoa.
	///
	/// OS CORPOS SAO AS DUAS COBAIAS (lado a lado, a 100 tiles do resto), e tudo o que a secao muda nelas -- a
	/// historia, a idade, a classe, o poder, o cargo `turtle` -- volta no `finally`.
	/// ==========================================================================================================
	/// </summary>
	private void OsDetalhesAoVivo(ServerPlayer eu, ServerPlayer ela, List<string> avisos, Action<string, bool, string> Checa)
	{
		// A HISTORIA SUJA: o caractere que inverte a direcao da linha (U+202E), um nulo, quatro linhas em branco
		// seguidas e uma tabulacao -- o que pode estar num save escrito antes de a limpeza existir. E a mesma
		// historia como OUTRO jogador pode le-la.
		const string suja = "Cresci numa vila de pescadores.\u202E\0\n\n\n\nPerdi meu mestre cedo.\tHoje treino sozinha.";
		const string limpa = "Cresci numa vila de pescadores.\n\nPerdi meu mestre cedo. Hoje treino sozinha.";
		const string outraVida = "Mudei de vida. Agora sou outra pessoa.";
		const string cargoDeTeste = "turtle";
		const double poderCanario = 7_654_321;

		int minimo = Convivio.ConvivioParaDetalhes;
		int teto = Jandirus.Core.Races.CharacterDraft.BackstoryMax;
		string nomeDoCargo = Jandirus.Core.Ranks.Cargos.Get(cargoDeTeste)?.Nome ?? "";

		bool haviaDono = _tronos.TryGetValue(cargoDeTeste, out string? donoDeVerdade);
		(string Historia, int Idade, string Genero, string Classe, double Bp, Vec2 Pos) deAntes =
			(ela.Historia, ela.Idade, ela.Genero, ela.Ficha.Class, ela.Ficha.BP, ela.Pos);
		List<(int Para, byte[] Pacote)>? escutaDeFora = EscutaDeDetalhes;
		var saidos = new List<(int Para, byte[] Pacote)>();
		EscutaDeDetalhes = saidos;
		Checagem Mutar = (n, c, d) => Checa(n, c, d);

		// O PACOTE, lido pelo MESMO leitor do cliente. `Sobrou` = os bytes que ele nao consumiu.
		static (Protocol.S2C Opcode, DetalhesDeConhecido D, int Sobrou) Abrir(byte[] pacote)
		{
			var r = new NetDataReader(pacote);
			var opcode = (Protocol.S2C)r.GetByte();
			DetalhesDeConhecido d = FioDosConhecidos.LerDetalhes(r);
			return (opcode, d, r.AvailableBytes);
		}

		static bool Contem(byte[] onde, byte[] oQue)
		{
			if (oQue.Length == 0) return false;
			for (int i = 0; i + oQue.Length <= onde.Length; i++)
				if (onde.AsSpan(i, oQue.Length).SequenceEqual(oQue)) return true;
			return false;
		}

		// SO OS DOIS ANDAM: o prazo do passo e por pessoa (ver `UmPasso`, na secao 1).
		void Passo()
		{
			eu.ProximaAproximacao = 0;
			ela.ProximaAproximacao = 0;
			TickDoConvivio();
		}

		// "UM MINUTO DEPOIS": a foto e os detalhes sao refeitos no maximo uma vez por minuto, e a bancada roda
		// inteira num quadro -- os dois carimbos da ficha sao puxados pra tras, o mesmo gesto do prazo do passo.
		void PassaUmMinuto()
		{
			if (eu.Social.Ficha(ela.Assinatura) is not { } f) return;
			f.VistoEm -= Convivio.IntervaloDoRetratoMs + 1;
			if (f.DetalhesEm != 0) f.DetalhesEm -= Convivio.IntervaloDoRetratoMs + 1;
		}

		// O PEDIDO, e o que saiu por causa dele (nulo = o servidor nao mandou pacote nenhum).
		DetalhesDeConhecido? Pedir()
		{
			saidos.Clear();
			VerboDetalhes(eu, ela.Assinatura);
			return saidos.Count == 1 && saidos[0].Para == eu.Id ? Abrir(saidos[0].Pacote).D : null;
		}

		try
		{
			eu.Social.Conhecidos.Clear();
			ela.Social.Conhecidos.Clear();
			avisos.Clear();

			// QUEM ELA E: 31 anos, mulher, Turtle Hermit, com a historia suja. E os CANARIOS -- a classe mais
			// rara do jogo e um poder de luta que nao se parece com mais nada do pacote.
			ela.Idade = 31;
			ela.Genero = "Female";
			ela.Historia = suja;
			_tronos[cargoDeTeste] = ela.Conta;
			ela.Ficha.Class = "Legendary";
			ela.Ficha.BP = poderCanario;

			// ---------------- 1. QUEM ACABOU DE APARECER ----------------
			Passo();
			if (eu.Social.Ficha(ela.Assinatura) is not { } ficha)
			{
				Checa("(preparo) um passo lado a lado poe a pessoa na minha lista", false, "sem ficha");
				return;
			}
			Checa("(preparo) um passo lado a lado: ela entra na minha lista, com convivio zero e a data do primeiro encontro",
				  ficha is { Familiaridade: 0, DesdeMs: > 0 }, $"convivio {ficha.Familiaridade}, desde {ficha.DesdeMs}");

			DetalhesDeConhecido? cedo = Pedir();
			Checa("**com convivio zero o servidor NAO manda os detalhes** (pacote nenhum nasce)",
				  cedo == null && saidos.Count == 0, $"{saidos.Count} pacote(s)");
			Checa($"...e diz o que falta, com os dois numeros (pede {minimo}, voces tem 0)",
				  avisos.Any(a => a.Contains("ainda não conviveram o bastante") && a.Contains($"{minimo} de convívio") && a.Contains("têm 0")),
				  string.Join(" | ", avisos));
			Checa("...e a historia e o cargo dela NEM FORAM COPIADOS pra minha ficha (o que nao esta guardado nao tem como vazar)",
				  ficha is { Historia: "", Titulo: "", DetalhesEm: 0 }, $"'{ficha.Titulo}', carimbo {ficha.DetalhesEm}");

			avisos.Clear();
			saidos.Clear();
			VerboDetalhes(eu, "0000000000");
			VerboDetalhes(eu, "");
			Checa("de quem eu NAO conheco (ou sem dizer de quem) nao sai nada, e o servidor responde que eu nao a conheco",
				  saidos.Count == 0 && avisos.Count(a => a.Contains("não conhece essa pessoa")) == 2, string.Join(" | ", avisos));
			Checa("...e `detalhes` e um verb DESTE canal (o funil nao segue pra 'comando desconhecido')",
				  ComandoDeConvivio(eu, "detalhes", "0000000000") && saidos.Count == 0, "");

			// ---------------- 2. UM PONTO ABAIXO DO MINIMO, com o defeito injetado ----------------
			// O criterio passa pelos DOIS pontos em que o portao e perguntado: a foto do tique (que guardaria a
			// historia na ficha) e o verb (que mandaria o pacote). Com o defeito ligado os dois abrem.
			bool AbaixoDoMinimoNadaSai()
			{
				ficha.Familiaridade = minimo - 1;
				ficha.PassosPerto = 0;
				ficha.Historia = "";
				ficha.Titulo = "";
				ficha.DetalhesEm = 0;
				Passo();
				return Pedir() == null && saidos.Count == 0 && ficha is { Historia: "", DetalhesEm: 0 };
			}

			Mutacao(Mutar,
				$"**com {minimo - 1} de convivio (um ponto abaixo do minimo) o servidor nao manda nada, nem guarda a historia**, "
				+ "pelo tique e pelo verb de verdade",
				"os detalhes saem com qualquer convivio (`Convivio.DetalhesSemConvivioDeTeste`)",
				AbaixoDoMinimoNadaSai,
				() => Convivio.DetalhesSemConvivioDeTeste = true,
				() => Convivio.DetalhesSemConvivioDeTeste = false);

			avisos.Clear();
			Pedir();
			Checa($"...e a recusa diz os dois numeros (pede {minimo}, voces tem {minimo - 1})",
				  avisos.Any(a => a.Contains($"{minimo} de convívio") && a.Contains($"têm {minimo - 1}")), string.Join(" | ", avisos));

			// ---------------- 3. O PONTO QUE PAGA, pelo tique ----------------
			ficha.PassosPerto = Convivio.PassosPertoPorPonto - 1;
			avisos.Clear();
			Passo();
			Checa($"**o passo que rende o ponto {minimo} ja traz os detalhes pra ficha**: a historia LIMPA, o cargo e o carimbo",
				  ficha.Familiaridade == minimo && ficha.Historia == limpa && ficha.Titulo == nomeDoCargo
				  && nomeDoCargo.Length > 0 && ficha.DetalhesEm > 0,
				  $"convivio {ficha.Familiaridade}, cargo '{ficha.Titulo}', carimbo {ficha.DetalhesEm}, historia de {ficha.Historia.Length} chars");

			DetalhesDeConhecido? veio = Pedir();
			Checa("**com o convivio minimo o servidor MANDA: um pacote, so pra quem pediu, e sem frase de recusa**",
				  veio != null && saidos.Count == 1 && !avisos.Any(a => a.Contains("conviveram o bastante")),
				  $"{saidos.Count} pacote(s); {string.Join(" | ", avisos)}");
			if (veio is not { } d) return;
			byte[] bytes = saidos[0].Pacote;
			(Protocol.S2C opcode, _, int sobrou) = Abrir(bytes);

			Checa("...com o opcode dos detalhes, e o leitor do cliente consome o pacote INTEIRO (nao ha campo alem dos publicos)",
				  opcode == Protocol.S2C.DetalhesDeConhecido && sobrou == 0, $"{opcode}, sobraram {sobrou} byte(s) de {bytes.Length}");
			Checa("...de quem e (a assinatura da lista), a IDADE e o genero, como eu a vi",
				  d.Assinatura == ela.Assinatura && d.Idade == 31 && d.Genero == "Female", $"{d.Idade} / {d.Genero}");
			Checa("...o CARGO que ela carrega, pelo nome", d.Titulo == nomeDoCargo, $"'{d.Titulo}' (esperado '{nomeDoCargo}')");
			Checa("**...e a HISTORIA que ela escreveu, LIMPA**: sem o caractere que inverte a linha, sem o nulo, com as linhas "
				  + "em branco juntas e a tabulacao virada espaco",
				  d.OuviuAHistoria && d.Historia == limpa, $"{d.Historia.Length} chars (esperado {limpa.Length})");
			Checa("...e ha quanto tempo eu a conheco e a vi (no minimo 1 s: zero no fio quer dizer 'nao sei')",
				  d.ConheceHaSegundos >= 1 && d.VistoHaSegundos >= 1, $"{d.ConheceHaSegundos} s / {d.VistoHaSegundos} s");

			// ---------------- 4. NADA SIGILOSO NO PACOTE ----------------
			// DUAS MEDIDAS, e uma cobre o que a outra nao ve: a lista FECHADA dos campos (com o "consumiu inteiro"
			// de cima, um campo novo no fio reprova) e os CANARIOS (um valor sigiloso escrito DENTRO de um campo
			// publico -- a classe no lugar do cargo -- tambem reprova).
			string campos = string.Join(",", typeof(DetalhesDeConhecido).GetProperties().Select(pr => pr.Name).Order(StringComparer.Ordinal));
			Checa("**o que viaja e uma lista FECHADA**: assinatura, genero, idade, cargo, historia (e se ja a ouvi) e duas datas",
				  campos == "Assinatura,ConheceHaSegundos,Genero,Historia,Idade,OuviuAHistoria,Titulo,VistoHaSegundos", campos);

			(string OQue, byte[] Bytes)[] canarios =
			[
				("a classe", System.Text.Encoding.UTF8.GetBytes(ela.Ficha.Class)),
				("a conta", System.Text.Encoding.UTF8.GetBytes(ela.Conta)),
				("o planeta onde ela esta", System.Text.Encoding.UTF8.GetBytes(ela.Zone.Name)),
				("o poder de luta (escrito)", System.Text.Encoding.UTF8.GetBytes(((long)poderCanario).ToString())),
				("o poder de luta (o numero)", BitConverter.GetBytes(poderCanario)),
			];
			Checa("CONTROLE: os canarios existem (ha o que procurar) e a busca acha o que ESTA la (uma palavra da historia)",
				  canarios.All(c => c.Bytes.Length > 0) && Contem(bytes, System.Text.Encoding.UTF8.GetBytes("pescadores")),
				  string.Join(",", canarios.Select(c => $"{c.OQue}:{c.Bytes.Length}")));
			string vazou = string.Join(", ", canarios.Where(c => Contem(bytes, c.Bytes)).Select(c => c.OQue));
			Checa("**nada sigiloso no pacote**: nem a classe, nem a conta, nem o lugar, nem o poder de luta estao em byte nenhum dele",
				  vazou.Length == 0, vazou);

			// ---------------- 5. E FOTOGRAFIA ----------------
			// Ela envelhece, perde o cargo e reescreve a historia -- LONGE de mim. Passa o minuto da foto, e o
			// que eu sei continua sendo o que eu vi.
			ela.Idade = 45;
			ela.Historia = outraVida;
			_tronos.Remove(cargoDeTeste);
			ela.Pos = eu.Pos + new Vec2(40 * ZoneCollision.TileSize, 0);
			PassaUmMinuto();
			Passo();
			DetalhesDeConhecido? deLonge = Pedir();
			Checa("**o que sai e a FOTOGRAFIA**: ela envelheceu, perdeu o cargo e reescreveu a historia longe de mim, e eu "
				  + "continuo sabendo o que VI (os detalhes nao contam o que alguem virou do outro lado do mundo)",
				  deLonge is { Idade: 31, Historia: limpa } && deLonge.Value.Titulo == nomeDoCargo,
				  deLonge is { } l ? $"{l.Idade} anos, cargo '{l.Titulo}'" : "sem pacote");

			ela.Pos = deAntes.Pos;
			Passo();
			DetalhesDeConhecido? dePerto = Pedir();
			Checa("...e no proximo encontro a ficha e refeita: 45 anos, sem cargo, a historia nova",
				  dePerto is { Idade: 45, Titulo: "", Historia: outraVida, OuviuAHistoria: true },
				  dePerto is { } p2 ? $"{p2.Idade} anos, cargo '{p2.Titulo}'" : "sem pacote");

			// ---------------- 6. O TEXTO GRANDE DEMAIS ----------------
			ela.Historia = new string('z', 3 * teto);
			PassaUmMinuto();
			Passo();
			DetalhesDeConhecido? grande = Pedir();
			Checa($"uma historia de {3 * teto} caracteres (o triplo do que a criacao aceita) sai CORTADA em {teto}",
				  grande is { Historia.Length: > 0 } && grande.Value.Historia.Length == teto,
				  grande is { } g ? $"{g.Historia.Length} chars" : "sem pacote");

			ela.Historia = new string('\u8A9E', teto);   // 500 caracteres de TRES bytes cada
			PassaUmMinuto();
			Passo();
			DetalhesDeConhecido? larga = Pedir();
			Checa($"...e a de {teto} caracteres de tres bytes cada ({3 * teto} bytes no fio) chega INTEIRA: o teto do leitor "
				  + "conta caracteres, e nao bytes",
				  larga is { } lg && lg.Historia == ela.Historia, larga is { } lg2 ? $"{lg2.Historia.Length} chars" : "sem pacote");

			// A METADE SOLTA DE UM PAR SUBSTITUTO (um save editado a mao, um disco estragado): o escritor do fio e
			// UTF-8 estrito e LANCA EXCECAO com ela. Sem a limpeza, pedir os detalhes desta pessoa derrubaria o
			// verb no meio -- e a excecao aqui dentro viraria a linha vermelha "a bancada rodou inteira".
			ela.Historia = "Texto com meio caractere:\uD83D fim.";
			PassaUmMinuto();
			Passo();
			DetalhesDeConhecido? quebrada = Pedir();
			Checa("...e a historia com a metade solta de um par substituto (que o fio se recusa a escrever) sai sem ela, e sai",
				  quebrada is { Historia: "Texto com meio caractere: fim." }, quebrada is { } q ? $"{q.Historia.Length} chars" : "sem pacote");

			// E A FICHA QUE JA ESTA SUJA (ela mora num save, e um arquivo pode ter sido escrito por outra mao): a
			// saida limpa de novo, sem esperar o proximo encontro refazer a ficha.
			ficha.Historia = "Do arquivo:\uDE00‮ fim.";
			DetalhesDeConhecido? doArquivo = Pedir();
			Checa("...e a historia que ja esta SUJA na ficha guardada e limpa de novo na saida (a ultima porta e o verb)",
				  doArquivo is { Historia: "Do arquivo: fim." }, doArquivo is { } da ? $"{da.Historia.Length} chars" : "sem pacote");

			// ---------------- 7. A LUTA TAMBEM PAGA, E NA HORA ----------------
			ela.Historia = suja;
			eu.Social.Conhecidos.Clear();
			ela.Social.Conhecidos.Clear();
			ConvivioPorDerrota(eu, ela, morreu: false);
			DetalhesDeConhecido? umaQueda = Pedir();
			Checa($"quem me derrubou UMA vez ({Convivio.FamiliaridadePorQueda} de convivio) ainda nao me deve historia nenhuma",
				  umaQueda == null && eu.Social.Ficha(ela.Assinatura) is { Historia: "", DetalhesEm: 0 },
				  $"convivio {eu.Social.Familiaridade(ela.Assinatura)}");
			ConvivioPorDerrota(eu, ela, morreu: false);
			DetalhesDeConhecido? duasQuedas = Pedir();
			Checa($"...na SEGUNDA queda ({2 * Convivio.FamiliaridadePorQueda}) os detalhes abrem -- e a historia ja esta la "
				  + "(os dois estao um na frente do outro: a ficha aprende na mesma hora)",
				  duasQuedas is { OuviuAHistoria: true, Historia: limpa },
				  $"convivio {eu.Social.Familiaridade(ela.Assinatura)}; {(duasQuedas == null ? "sem pacote" : "com pacote")}");
		}
		finally
		{
			Convivio.DetalhesSemConvivioDeTeste = false;
			EscutaDeDetalhes = escutaDeFora;
			if (haviaDono) _tronos[cargoDeTeste] = donoDeVerdade!;
			else _tronos.Remove(cargoDeTeste);
			ela.Historia = deAntes.Historia;
			ela.Idade = deAntes.Idade;
			ela.Genero = deAntes.Genero;
			ela.Ficha.Class = deAntes.Classe;
			ela.Ficha.BP = deAntes.Bp;
			ela.Pos = deAntes.Pos;
			eu.Social.Conhecidos.Clear();
			ela.Social.Conhecidos.Clear();
		}
	}

	/// <summary>
	/// A PORTA DE BP **DESTE CORPO** PRA ESTA FORMA -- o limiar pessoal quando ha um, o do catalogo
	/// quando nao ha (a mesma conta de `GameServer.Tecnicas.G4:736` e `GameServer.Oozaru.cs:122`).
	///
	/// A bancada precisa dela pra ESCOLHER um BP entre dois degraus. Escolher pelo numero do catalogo
	/// daria um BP errado em todo personagem cujo `restssjat` foi sorteado alto -- e uma bancada que
	/// reprova por sorteio e pior que nenhuma: ela ensina a ignorar a cor vermelha.
	/// </summary>
	private static double PortaDeTeste(ServerPlayer pl, string id)
	{
		FormaDef? d = Catalogo.Def(id);
		if (d == null) return 0;
		return pl.Forma.Limiares?.Porta(d) is > 0 and var p ? p : d.PortaBp;
	}

	/// <summary>
	/// ============================ O CONVIVIO ATRAVESSA O DISCO? E O SAVE DE ANTES? ============================
	/// Esta e a checagem que uma bancada de Core NAO pode fazer, e ela ja teria pego um defeito
	/// real deste projeto: as cores de roupa foram escritas, lidas e usadas por meses **sem nunca
	/// persistir**, porque o campo era `readonly` e o `System.Text.Json` o ignorava em silencio.
	/// O mesmo silencio aqui apagaria a lista de conhecidos de todo mundo a cada logout -- e o sintoma
	/// seria "o SSJ1 as vezes nao vem", que ninguem liga a um serializador.
	///
	/// Ela grava e le pelo `AccountStore` DE VERDADE, com as opcoes de verdade, numa pasta
	/// temporaria que e apagada no fim. Reimplementar as opcoes aqui testaria a bancada.
	///
	/// A SEGUNDA METADE E O SAVE DE ANTES DE 2026-10-10: o arquivo que acabou de ser gravado ganha, a mao,
	/// as tres listas da afinidade (`Amizade`, `Inimizade`, `Rivais`) -- que e como esta o disco de todo
	/// jogador que ja jogava --, e o caminho do login (`ParaJogador` + `ConhecidosNoLogin`) tem que carregar,
	/// virar o tempo vivido em convivio, avisar o jogador e parar de gravar o campo velho.
	/// =======================================================================================
	/// </summary>
	private void ASobrevivenciaNoDisco(ServerPlayer amigo, ServerPlayer vitima, ServerPlayer algoz,
									   List<string> avisos, Action<string, bool, string> Checa)
	{
		string pasta = Path.Combine(Path.GetTempPath(), "jandirus_convivio_" + Guid.NewGuid().ToString("N"));
		try
		{
			var loja = new AccountStore(pasta);
			var conta = new AccountSave { Conta = "bancada_convivio" };

			// um passo de proximidade guardado, pra medir que ele tambem vai pro disco
			Conhecido fichaViva = amigo.Social.Ficha(vitima.Assinatura)!;
			fichaViva.PassosPerto = 7;
			// e os DETALHES ouvidos (a historia, o cargo, o carimbo), que sao estado da ficha como o resto
			fichaViva.Historia = "Uma historia ouvida ao lado dela.";
			fichaViva.Titulo = "Turtle Hermit";
			fichaViva.DetalhesEm = 123_456;

			conta.Slots[0] = AccountStore.DeJogador(amigo, 0);
			loja.Gravar(conta);

			AccountSave? volta = loja.Carregar("bancada_convivio");
			Convivio? social = volta?.Slots[0]?.Social;

			Checa("o convivio volta do disco (a conta e o slot foram lidos)", social != null, "nulo");
			if (social == null) return;

			Checa("...com a ficha de quem foi visto",
				  social.Ficha(vitima.Assinatura)?.Nome == vitima.Name,
				  social.Ficha(vitima.Assinatura)?.Nome ?? "sem ficha");
			Checa("...com o CONVIVIO intacto, e os passos guardados junto",
				  social.Familiaridade(vitima.Assinatura) == amigo.Social.Familiaridade(vitima.Assinatura)
				  && social.Familiaridade(vitima.Assinatura) >= 50
				  && social.Ficha(vitima.Assinatura)?.PassosPerto == 7,
				  $"{social.Familiaridade(vitima.Assinatura)}, passos {social.Ficha(vitima.Assinatura)?.PassosPerto}");
			Checa("...com os DETALHES que eu ja tinha ouvido dela (a historia, o cargo) e desde quando a conheco",
				  social.Ficha(vitima.Assinatura) is { Historia: "Uma historia ouvida ao lado dela.", Titulo: "Turtle Hermit", DetalhesEm: 123_456 } lida
				  && lida.DesdeMs == fichaViva.DesdeMs && fichaViva.DesdeMs > 0,
				  $"'{social.Ficha(vitima.Assinatura)?.Titulo}', desde {social.Ficha(vitima.Assinatura)?.DesdeMs} (era {fichaViva.DesdeMs})");
			Checa("...com a DECLARACAO, e ainda dizendo que aquela pessoa e AMIGA (e o que a raiva le)",
				  social.RelacaoCom(vitima.Assinatura) == Relacao.Bom && social.EhAmigo(vitima.Assinatura)
				  && social.RelacaoCom(algoz.Assinatura) == Relacao.MuitoBom,
				  string.Join(",", social.Conhecidos.Values.Select(c => $"{c.Nome}:{c.Familiaridade}:{c.Relacao}")));

			string arquivo = Path.Combine(pasta, AccountStore.NomeDeArquivo("bancada_convivio") + ".json");
			string texto = File.ReadAllText(arquivo);
			Checa("**o save de hoje nao grava afinidade nenhuma** (nem `Amizade`, nem `Inimizade`, nem `Rivais`)",
				  !texto.Contains("\"Amizade\"") && !texto.Contains("\"Inimizade\"") && !texto.Contains("\"Rivais\""), "");

			// ---------------- O SAVE DE ANTES ----------------
			// As tres listas velhas entram no JSON gravado, logo antes da lista de fichas: 120 pontos de
			// afinidade com a vitima (uma hora lado a lado), -60 com o algoz (o "inimigo" automatico), o odio
			// e a rivalidade que vinham junto, e 77 pontos com alguem sem ficha.
			const string marca = "\"Conhecidos\": {";
			int onde = texto.IndexOf(marca, StringComparison.Ordinal);
			Checa("(preparo) achei a lista de fichas no arquivo gravado", onde >= 0, "");
			if (onde < 0) return;
			string velhas = $"\"Amizade\": {{ \"{vitima.Assinatura}\": 120.4, \"{algoz.Assinatura}\": -60, \"9999999999\": 77 }}, "
						  + $"\"Inimizade\": {{ \"{algoz.Assinatura}\": 60 }}, \"Rivais\": [ \"{algoz.Assinatura}\" ], ";

			// E A CLASSE: a ficha de antes guardava a classe de cada conhecido. O campo saiu (a classe nunca
			// aparece -- `GameServer.Sigilo.cs`, regra 2), e o membro que sobrou no arquivo tem que ser ignorado.
			string daVitima = $"\"Assinatura\": \"{vitima.Assinatura}\",";
			int naFicha = texto.IndexOf(daVitima, onde, StringComparison.Ordinal);
			Checa("(preparo) achei a ficha da vitima no arquivo gravado", naFicha >= 0, "");
			if (naFicha < 0) return;
			File.WriteAllText(arquivo, texto[..onde] + velhas + texto[onde..naFicha]
									   + "\"Classe\": \"ClasseDeAntes\", " + texto[naFicha..]);

			AccountSave? antiga = loja.Carregar("bancada_convivio");
			CharacterSave? fichaAntiga = antiga?.Slots[0];
			Checa("**o save ANTIGO (com as tres listas da afinidade, e a classe em cada ficha) carrega**",
				  fichaAntiga?.Social?.Ficha(vitima.Assinatura)?.Nome == vitima.Name, "a conta voltou ilegivel, ou sem a ficha");
			if (antiga == null || fichaAntiga?.Social == null) return;
			Checa("...e a carga ainda traz os pontos velhos (a migracao e do LOGIN, nao do leitor de arquivo)",
				  fichaAntiga.Social.Amizade is { Count: 3 }, $"{fichaAntiga.Social.Amizade?.Count}");

			// O CAMINHO DO LOGIN: o save vira corpo, e a lista vai pro jogador
			var renascido = new ServerPlayer
			{
				Id = IdBaseDoConvivioDeTeste + 9, Peer = null, Conta = amigo.Conta, Slot = 0, Zone = amigo.Zone,
				Ficha = new Jandirus.Core.Stats.Fighter { Race = "Saiyan", BP = 1000 },
			};
			AccountStore.ParaJogador(fichaAntiga, renascido);
			int convAntes = renascido.Social.Familiaridade(vitima.Assinatura);
			int comOAlgoz = renascido.Social.Familiaridade(algoz.Assinatura);
			avisos.Clear();
			ConhecidosNoLogin(renascido);

			Checa("**no login, o tempo vivido vira convivio: os 120 pontos somam no que ja havia**",
				  renascido.Social.Familiaridade(vitima.Assinatura) == convAntes + 120,
				  $"{convAntes} -> {renascido.Social.Familiaridade(vitima.Assinatura)}");
			Checa("...o negativo (o 'inimigo' que o jogo declarava) nao vira nada",
				  renascido.Social.Familiaridade(algoz.Assinatura) == comOAlgoz,
				  $"{comOAlgoz} -> {renascido.Social.Familiaridade(algoz.Assinatura)}");
			Checa("...as declaracoes do jogador nao foram tocadas, e quem tinha pontos sem ficha nao ganhou ficha",
				  renascido.Social.RelacaoCom(vitima.Assinatura) == Relacao.Bom
				  && renascido.Social.RelacaoCom(algoz.Assinatura) == Relacao.MuitoBom
				  && !renascido.Social.Conhece("9999999999"), "");
			Checa("...e o jogador e AVISADO, uma vez, de que a aba People mudou",
				  avisos.Count(a => a.Contains("a aba People mudou")) == 1, string.Join(" | ", avisos));
			avisos.Clear();
			ConhecidosNoLogin(renascido);
			Checa("...e no login seguinte nao ha nem migracao nem aviso",
				  !avisos.Any(a => a.Contains("a aba People mudou"))
				  && renascido.Social.Familiaridade(vitima.Assinatura) == convAntes + 120, "");

			// O `Social` do corpo E o do save carregado (o `ParaJogador` nao copia), entao regravar a conta
			// grava exatamente o que o proximo `DeJogador` gravaria deste personagem.
			loja.Gravar(antiga);
			string regravado = File.ReadAllText(arquivo);
			Checa("**regravado depois do login, o arquivo nao carrega mais as listas velhas -- nem a classe de ninguem**",
				  !regravado.Contains("\"Amizade\"") && !regravado.Contains("\"Inimizade\"") && !regravado.Contains("\"Rivais\"")
				  && !regravado.Contains("ClasseDeAntes"), "");
		}
		catch (Exception e)
		{
			Checa("o convivio atravessa o disco", false, e.Message);
		}
		finally
		{
			try { if (Directory.Exists(pasta)) Directory.Delete(pasta, recursive: true); }
			catch (Exception e) { GD.Print($"[bancada] nao consegui apagar {pasta}: {e.Message}"); }
		}
	}

	/// <summary>Roda o gesto e devolve a pergunta -- so pra escrever o contra-exemplo numa linha.</summary>
	private static bool Verificando(Action gesto, Func<bool> pergunta) { gesto(); return pergunta(); }
}
