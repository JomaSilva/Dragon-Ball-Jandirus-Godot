using Godot;
using Jandirus.Core.Forms;
using Jandirus.Core.Social;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// BANCADA AO VIVO DO CONVIVIO -- roda dentro do `--formasteste`.
///
/// ============================ O QUE SO DAQUI SE VE ============================
/// As REGRAS do convivio sao do Core (`Convivio`), e uma bancada de Core prova todas elas com
/// dicionarios na mao. O que ela **nao** consegue provar e a CORRENTE, que e o assunto desta
/// sessao inteira e onde este port ja quebrou muitas vezes:
///
///   conviver -> ser amigo -> ver o amigo morrer -> entrar em furia -> **a tecla C sair da base**.
///
/// Cada elo mora num arquivo diferente (Core, `GameServer.Convivio.cs`, `GameServer.Formas.cs`), e
/// os tres podem estar certos com a corrente arrebentada no meio. Foi exatamente esse o estado do
/// projeto ate agora: o `AmigoAbatido` existia, tinha bancada propria, e **nao tinha chamador** --
/// o SSJ1 so saia por verb de admin e nada no projeto acusava.
///
/// E ELA E O AVESSO DA ANTIGA SECAO [8] DA BANCADA `raiva`, que varria os fontes pra provar que o
/// gancho estava MUDO. Aquela virou "o gancho tem dono"; esta prova que o dono FUNCIONA.
/// ==============================================================================
///
/// ============================ OS CORPOS SAO FORJADOS ============================
/// Quatro corpos inventados, numa zona pre-feita onde nao ha ninguem conectado -- o mesmo padrao do
/// <see cref="MedirCongelamentoNaCena"/>, e pelo mesmo motivo: isto mata gente, poe gente em furia
/// e mexe em forma, e quem esta rodando a bancada tem que terminar como comecou.
///
/// A DIFERENCA E QUE ELES ENTRAM NO `_players` E NA `ZoneList`, e precisam entrar: o luto varre a
/// zona, e um corpo fora da lista da zona nao e visto por ninguem -- a bancada daria verde por
/// ausencia. Entram e saem DENTRO do mesmo bloco sincrono (o `--formasteste` roda dentro de um
/// quadro do servidor), entao nenhum tique do jogo chega a ve-los.
/// ================================================================================
/// </summary>
public partial class GameServer
{
	/// <summary>Faixa de ids de bancada -- bem longe do `_nextId`, que comeca em 1.</summary>
	private const int IdBaseDoConvivioDeTeste = 90_100;

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
		// no `RaioDeTestemunha` (22) de ninguem -- senao as mortes da secao 10 mexeriam nos lacos que
		// as secoes 1-9 acabaram de medir.
		ServerPlayer cobaiaA = Forjar(6, "bancada: a cobaia", 100 * ZoneCollision.TileSize);
		ServerPlayer cobaiaB = Forjar(7, "bancada: o carrasco", 101 * ZoneCollision.TileSize);

		try
		{
			// ============================ 1. **CONVIVER FAZ AMIGO** (a divergencia do dono) ============================
			// *"AQUI PODERIA SER SOMENTE alguem q ficou BASTANTE TEMPO COM VC ate virar amigo"*. O
			// numero e 500 passos de 3 s = 25 minutos, e o que esta secao mede e o DEGRAU: 499 passos
			// ainda nao sao amizade, e o 500 e. So o lado de cima ("depois de muito tempo eles sao
			// amigos") passaria igual num sistema que faz amigo no primeiro segundo.
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

			for (int i = 0; i < 499; i++) UmPasso();

			double pts = amigo.Social.PontosDeAmizade(vitima.Assinatura);
			Checa("**um passo antes dos 25 minutos, conviver ainda NAO fez amigo**",
				  !amigo.Social.EhAmigo(vitima.Assinatura) && pts > 0,
				  $"{pts:0.###} -> {Convivio.RotuloDeProximidade(pts)}");

			UmPasso();
			pts = amigo.Social.PontosDeAmizade(vitima.Assinatura);
			Checa("**e no passo 500 (25 min a menos de 6 tiles) eles sao amigos, sem verb nenhum**",
				  amigo.Social.EhAmigo(vitima.Assinatura), $"{pts:0.###}");
			Checa("...e a travessia foi MUTUA -- os dois lados cruzaram o limiar",
				  vitima.Social.EhAmigo(amigo.Assinatura),
				  $"{vitima.Social.PontosDeAmizade(amigo.Assinatura):0.###}");
			Checa("...e o rotulo que a aba escreve e `Amigo`",
				  Convivio.RotuloDeProximidade(pts) == "Amigo", Convivio.RotuloDeProximidade(pts));
			Checa("...e quem estava a 40 tiles nao conheceu ninguem",
				  longe.Social.Conhecidos.Count == 0, $"{longe.Social.Conhecidos.Count}");
			Checa("...e a ficha do conhecido guarda a raca de quando ele foi visto",
				  amigo.Social.Ficha(vitima.Assinatura)?.Raca == "Saiyan",
				  amigo.Social.Ficha(vitima.Assinatura)?.Raca ?? "sem ficha");

			// ============================ 2. O PEDIDO CONTINUA SENDO O ATALHO ============================
			// Ele deixou de ser a UNICA porta (era o `49 < 50` do DM) e virou o atalho -- e a volta,
			// ver a secao 9b. PELOS VERBOS DE VERDADE, e nao escrevendo no dicionario: o caminho do
			// jogador passa pelo alcance (3 tiles), pelo pedido pendente e pela resposta, e e nele que
			// uma amizade de mao unica nasceria.
			//
			// ENTRE `longe` E `vitima` porque os dois **nunca conviveram** (o `longe` esta a 40 tiles
			// e a secao 1 acabou de provar que ele nao conhece ninguem): medir o atalho entre duas
			// pessoas que ja sao amigas pelo relogio nao mediria nada.
			longe.Pos = new Vec2(2 * ZoneCollision.TileSize, 0);   // entra no alcance de 3 tiles do pedido
			VerboPedirAmizade(vitima, longe.Id.ToString());
			Checa("o pedido de amizade fica pendurado em quem recebeu",
				  longe.PedidoDeAmizade == vitima.Assinatura, longe.PedidoDeAmizade);

			VerboResponderAmizade(longe, aceitou: true);
			Checa("aceito, ele faz AMIGO dos DOIS LADOS na hora (amizade de mao unica nao existe)",
				  longe.Social.EhAmigo(vitima.Assinatura) && vitima.Social.EhAmigo(longe.Assinatura),
				  $"{longe.Social.PontosDeAmizade(vitima.Assinatura):0} / "
				  + $"{vitima.Social.PontosDeAmizade(longe.Assinatura):0}");
			Checa("...e o pedido pendente foi consumido", longe.PedidoDeAmizade.Length == 0, "");

			// E ELE VOLTA PRA LONGE: as secoes seguintes usam o `longe` como a testemunha que **nao**
			// esta vendo, e como o algoz desconhecido. Deixa-lo colado quebraria as duas.
			longe.Pos = new Vec2(40 * ZoneCollision.TileSize, 0);
			longe.Social.Amizade.Remove(vitima.Assinatura);
			vitima.Social.Amizade.Remove(longe.Assinatura);
			longe.Social.Conhecidos.Clear();

			// ============================ 3. DUELO ENTRE AMIGOS NAO ENFURECE NINGUEM ============================
			// A trava que impede o treino de virar fabrica de Super Saiyajin (`Death.dm:75`). Aqui o
			// algoz e AMIGO da vitima -- e ninguem entra em luto, mesmo com o amigo assistindo.
			vitima.Social.AceitarAmizade(algoz.Assinatura);
			amigo.FuriaExtremaAte = 0;
			amigo.RaivaLendariaAte = 0;

			AoPerderALuta(vitima, algoz, morreu: true);
			Checa("amigo MORTO por um AMIGO dele: ninguem entra em furia",
				  Perfil(amigo).Raiva == NivelDeRaiva.Nenhuma, Perfil(amigo).Raiva.ToString());

			// ============================ 4. MORTO POR UM INIMIGO: O LUTO ============================
			vitima.Social.Amizade.Remove(algoz.Assinatura);   // o algoz deixa de ser amigo da vitima
			AoPerderALuta(vitima, algoz, morreu: true);

			Checa("amigo MORTO por um INIMIGO, na sua frente: FURIA EXTREMA",
				  Perfil(amigo).Raiva == NivelDeRaiva.Extrema, Perfil(amigo).Raiva.ToString());
			Checa("...e quem estava a 40 tiles nao viu nada (o 'na frente dele' e uma condicao)",
				  Perfil(longe).Raiva == NivelDeRaiva.Nenhuma, Perfil(longe).Raiva.ToString());
			Checa("...e quem MATOU nao se enfurece com o proprio crime",
				  Perfil(algoz).Raiva == NivelDeRaiva.Nenhuma, Perfil(algoz).Raiva.ToString());

			// ============================ 5. O NOCAUTE VALE O GRAU DE BAIXO ============================
			// Os DOIS graus tem chamador, e nao so a morte -- e o nocaute e o unico que a linha
			// Legendary precisa. Ligar so a morte deixaria metade da regra do dono sem dono.
			amigo.FuriaExtremaAte = 0;
			amigo.RaivaLendariaAte = 0;
			AoPerderALuta(vitima, algoz, morreu: false);
			Checa("amigo NOCAUTEADO por um inimigo: raiva LENDARIA (e nao a extrema)",
				  Perfil(amigo).Raiva == NivelDeRaiva.Lendaria, Perfil(amigo).Raiva.ToString());

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

			// so com a raiva LENDARIA no corpo (um amigo caiu), o tronco comum nao se move
			Transformar(amigo, subir: true);
			PassarACena(amigo);
			Checa("com o amigo so NOCAUTEADO, o Saiyajin comum continua na base",
				  amigo.Forma.NaBase, "chegou em " + amigo.Forma.Atual);

			// e com o LUTO ele sobe -- este e o SSJ1 alcancavel pelo caminho do jogador
			AoPerderALuta(vitima, algoz, morreu: true);
			amigo.Ficha.Ki = amigo.Ficha.MaxKi;
			Checa("com o amigo MORTO, o SSJ1 passa a ser alcancavel",
				  amigo.Forma.Avaliar("ssj1", amigo.Ficha.BP, 1, false, Perfil(amigo))
					  == RecusaForma.Pode,
				  amigo.Forma.Avaliar("ssj1", amigo.Ficha.BP, 1, false, Perfil(amigo)).ToString());

			Transformar(amigo, subir: true);
			PassarACena(amigo);
			Checa("**A TECLA C SAI DA BASE** -- o SSJ1 voltou a ser alcancavel sem admin",
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
			algoz.RaivaLendariaAte = 0;
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

			// AGORA O MESMO CORPO VE UM AMIGO MORRER. Quem mata e o `longe`, de quem a vitima nao e
			// amiga nem declarou nada -- e portanto e inimigo pelo `Death.dm:75`. (Ele chegou a ser
			// fotografado no pedido da secao 2; conhecer nao e gostar, e e a AMIZADE que a pergunta le.)
			algoz.Social.AceitarAmizade(vitima.Assinatura);
			vitima.Social.AceitarAmizade(algoz.Assinatura);
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
			algoz.RaivaLendariaAte -= (long)(SegundosDeRaiva * 1000) + 500;
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
			longe.RaivaLendariaAte = 0;
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
			// A regra do dono, palavra por palavra: *"o Legendary Saiyajin tem a skill `legendary
			// anger` e por isso precisa de MENOS: basta ver um amigo ser NOCAUTEADO"*. Ela ja existia
			// em tres lugares -- o `RaivaExigida` do Core, o `AmigoAbatido` chamado A MAO pela bancada
			// da raiva, e o `AoPerderALuta` daqui. Nenhum dos tres media a corrente inteira ATE O NOME
			// DA FORMA: a bancada da raiva confere a LINHA ("saiu um degrau Legendary"), e a linha tem
			// sete degraus.
			//
			// O QUE ESTA SECAO ACRESCENTA, e so ela pode:
			//   1. que o degrau que abre com o grau mais barato e o `wrathful` PELO NOME;
			//   2. que basta um NOCAUTE -- nao ha morte nenhuma aqui, e a bancada confere que a
			//      janela do LUTO ficou fechada enquanto a do nocaute abria;
			//   3. que o MESMO nocaute, no MESMO instante, nao move o Saiyajin comum ao lado.
			// ==========================================================================================================
			Checa("o grau que o `wrathful` cobra e o LENDARIO (o do nocaute), e nao o do luto",
				  Catalogo.RaivaExigida(Catalogo.Def(Catalogo.IdWrathful)) == NivelDeRaiva.Lendaria,
				  Catalogo.RaivaExigida(Catalogo.Def(Catalogo.IdWrathful)).ToString());

			// O BP SAI DO LIMIAR PESSOAL, e nao de um numero escrito aqui: `restssjat` e sorteado por
			// personagem (`LimiaresPessoais.RolarSaiyajin`), entao um `20_000_000` cravado funcionaria
			// em quase todo mundo e reprovaria calado no sorteio ruim. A media geometrica entre a porta
			// do `wrathful` e a do degrau seguinte deixa **um** degrau alcancavel -- e e isso que torna
			// o nome da forma uma medida em vez de um palpite sobre o que o `Proxima` escolhe.
			lenda.Ficha.Class = "Legendary";
			lenda.Ficha.Statify();
			double portaWrathful = PortaDeTeste(lenda, Catalogo.IdWrathful);
			double portaSeguinte = PortaDeTeste(lenda, "c_type");
			lenda.Ficha.BP = Math.Sqrt(portaWrathful * portaSeguinte);
			lenda.Ficha.Statify();
			lenda.Ficha.Ki = lenda.Ficha.MaxKi;
			lenda.Forma.Entrar(Catalogo.IdBase);
			lenda.Forma.Liberadas.Clear();
			lenda.Forma.EstreiaVista.Clear();
			lenda.FuriaExtremaAte = 0;
			lenda.RaivaLendariaAte = 0;
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
			lenda.Social.AceitarAmizade(vitima.Assinatura);

			// O SAIYAJIN COMUM DO CONTRASTE VOLTA A NAO TER DESPERTADO NADA -- e esta linha custou uma
			// rodada vermelha. Ele despertou o SSJ1 no passo 6, e forma despertada nao pergunta mais
			// pela raiva (`EstadoDeForma.cs:314`): ele subiria com o nocaute e a bancada leria isso
			// como "o desconto do Legendary nao existe". O que esta secao compara e o GATE, e quem ja
			// atravessou o gate nao tem gate pra medir.
			amigo.Forma.Entrar(Catalogo.IdBase);
			amigo.Forma.Liberadas.Clear();
			amigo.Forma.EstreiaVista.Clear();
			amigo.FuriaExtremaAte = 0;
			amigo.RaivaLendariaAte = 0;
			amigo.Ficha.Ki = amigo.Ficha.MaxKi;
			AplicarForma(amigo);

			AoPerderALuta(vitima, longe, morreu: false);
			lenda.Ficha.Ki = lenda.Ficha.MaxKi;

			Checa("o nocaute abre a janela LENDARIA e deixa a do LUTO fechada",
				  Perfil(lenda).Raiva == NivelDeRaiva.Lendaria && lenda.FuriaExtremaAte == 0,
				  $"{Perfil(lenda).Raiva}, luto ate {lenda.FuriaExtremaAte}");
			Checa("**com um nocaute so, o `wrathful` abre**",
				  lenda.Forma.Avaliar(Catalogo.IdWrathful, lenda.Ficha.BP, 1, false, Perfil(lenda))
					  == RecusaForma.Pode,
				  lenda.Forma.Avaliar(Catalogo.IdWrathful, lenda.Ficha.BP, 1, false, Perfil(lenda))
					  .ToString());

			SubirAteParar(lenda);
			Checa("...e a tecla C leva ao `wrathful` PELO NOME (e nao 'a algum degrau da linha')",
				  lenda.Forma.Atual == Catalogo.IdWrathful, "parou em " + lenda.Forma.Atual);
			Checa("...e o corpo recebeu o multiplicador da forma",
				  lenda.Ficha.ssjBuff > 1.4, $"ssjBuff {lenda.Ficha.ssjBuff:0.###}");

			// E O CONTRASTE, QUE E O QUE FAZ O DESCONTO EXISTIR: o Saiyajin comum viu o MESMO nocaute,
			// no MESMO instante, e continua na base. Sem esta linha, "a raiva lendaria abre o
			// Wrathful" seria uma frase sobre um enum.
			SubirAteParar(amigo);
			Checa("...enquanto o Saiyajin comum, que viu o MESMO nocaute, continua na base",
				  amigo.Forma.NaBase, "chegou em " + amigo.Forma.Atual);

			// ============================ 7. O ODIO E OUTRA MOEDA ============================
			// Ele NAO cresce por convivencia: so contra quem foi declarado rival. Sem esta
			// assimetria, todo servidor viraria uma teia de inimizades automaticas.
			//
			// ============================ ANTES, O RESCALDO DAS MORTES ACIMA ============================
			// As secoes 3 a 6 mataram a `vitima` na frente do `amigo` cinco vezes, e a regra nova cobra
			// isso: **o `amigo` ja e inimigo declarado do `algoz` sem ter clicado em nada**. Isto nao e
			// arrumacao de bancada, e a medida do sistema funcionando de ponta a ponta -- por isso ela e
			// uma CHECAGEM e nao um `Remove` calado. So depois o estado volta ao zero, porque o que a
			// secao 7 mede e o eixo do odio DECLARADO, e ele precisa comecar limpo pra dizer alguma coisa.
			Checa("**as mortes que ele assistiu ja fizeram do algoz um INIMIGO dele, sozinhas**",
				  amigo.Social.EhInimigo(algoz.Assinatura) && amigo.Social.EhRival(algoz.Assinatura),
				  $"{amigo.Social.PontosDeAmizade(algoz.Assinatura):0.###} / rival="
				  + $"{amigo.Social.EhRival(algoz.Assinatura)}");
			Checa("...e o algoz perdeu do MESMO jeito no livro dele (a perda e mutua)",
				  algoz.Social.EhInimigo(amigo.Assinatura),
				  $"{algoz.Social.PontosDeAmizade(amigo.Assinatura):0.###}");

			amigo.Social.Rivais.Remove(algoz.Assinatura);
			amigo.Social.Inimizade.Remove(algoz.Assinatura);
			amigo.Social.Amizade.Remove(algoz.Assinatura);

			amigo.Social.SomarInimizade(algoz.Assinatura, 50);
			Checa("odio nao cresce contra quem nao foi declarado rival",
				  amigo.Social.PontosDeInimizade(algoz.Assinatura) == 0,
				  $"{amigo.Social.PontosDeInimizade(algoz.Assinatura):0}");

			VerboRival(amigo, algoz.Id.ToString());
			Checa("o verb declara o rival", amigo.Social.EhRival(algoz.Assinatura), "");
			amigo.Social.SomarInimizade(algoz.Assinatura, 50);
			Checa("...e ai ele cresce", amigo.Social.PontosDeInimizade(algoz.Assinatura) == 50,
				  $"{amigo.Social.PontosDeInimizade(algoz.Assinatura):0}");
			Checa("...e rival declarado para de render amizade por proximidade",
				  RenderiaAmizade(amigo, algoz) == false, "");

			VerboRival(amigo, algoz.Id.ToString());
			Checa("e o mesmo verb desfaz a rivalidade", !amigo.Social.EhRival(algoz.Assinatura), "");

			// ============================ 8. A RELACAO E PAGA COM CONVIVIO ============================
			// A familiaridade so cresce CONVERSANDO (1% por fala ouvida), entao ela e a moeda cara
			// deste sistema. O verb confere no servidor -- apagar o botao no cliente nunca foi
			// permissao.
			Conhecido? ficha = amigo.Social.Ficha(algoz.Assinatura);
			if (ficha != null) ficha.Familiaridade = 0;
			VerboRelacao(amigo, $"{algoz.Assinatura}|muito bom");
			Checa("declarar 'muito bom' sem convivio nenhum e RECUSADO (pede 100)",
				  amigo.Social.RelacaoCom(algoz.Assinatura) == Relacao.Nenhuma,
				  amigo.Social.RelacaoCom(algoz.Assinatura).ToString());

			if (ficha != null) ficha.Familiaridade = 100;
			VerboRelacao(amigo, $"{algoz.Assinatura}|muito bom");
			Checa("...e com 100 de convivio ela passa",
				  amigo.Social.RelacaoCom(algoz.Assinatura) == Relacao.MuitoBom,
				  amigo.Social.RelacaoCom(algoz.Assinatura).ToString());

			// E A RELACAO E A **SEGUNDA PORTA** DA RAIVA -- a que nao depende de amizade nenhuma.
			// Portar so o `is_friend` teria deixado esta metade de fora.
			var semAmizade = new Convivio();
			semAmizade.Fotografar("x", "x", "x", "x", "x", 0, 0);
			semAmizade.Ficha("x")!.Relacao = Relacao.Bom;
			Checa("relacao 'bom' sozinha ja enfurece por MORTE (a segunda porta de `Death.dm:79`)",
				  semAmizade.LutoPorMorte("x"), "");
			Checa("...mas nao por NOCAUTE, que so aceita os dois graus mais altos (`KO.dm:36`)",
				  !semAmizade.LutoPorQueda("x"), "");

			// ============================ 8b. **O PRECO DE MATAR, PELO FUNIL DE VERDADE** ============================
			// A parte que nao e porte (no DM a amizade nunca desce), e ela e medida pelo
			// <see cref="AoPerderALuta"/> -- o mesmo funil por onde o Zenkai, o luto, o odio, a sucessao
			// e o karma ja passam. Escrever `Afastar` a mao aqui mediria o Core, que a bancada de Core
			// ja mede; o que so daqui se ve e que a consequencia esta PENDURADA no funil.
			//
			// O PAR E `amigo` x `algoz` de proposito: e o estado que a secao 9 leva pro disco, entao a
			// prova de que o inimigo persiste e a prova deste mesmo evento.
			amigo.Social.Amizade.Remove(algoz.Assinatura);
			amigo.Social.Rivais.Remove(algoz.Assinatura);
			amigo.Social.Inimizade.Remove(algoz.Assinatura);
			algoz.Social.Amizade.Remove(amigo.Assinatura);
			algoz.Social.Rivais.Remove(amigo.Assinatura);

			// (1) O SPAR: nocaute vindo de um AMIGO nao custa nada. **Sem esta trava o sistema seria
			// injogavel**: treinar com o melhor amigo derrubaria o vinculo 25 pontos por queda, e duas
			// horas de treino transformariam dois `Ligados` em inimigos por terem treinado juntos.
			amigo.Social.AceitarAmizade(algoz.Assinatura);
			algoz.Social.AceitarAmizade(amigo.Assinatura);
			double laco = amigo.Social.PontosDeAmizade(algoz.Assinatura);
			double plateia = vitima.Social.PontosDeAmizade(algoz.Assinatura);

			AoPerderALuta(amigo, algoz, morreu: false);
			Checa("**nocaute de um AMIGO (o spar) nao custa um ponto de amizade**",
				  amigo.Social.PontosDeAmizade(algoz.Assinatura) == laco,
				  $"{amigo.Social.PontosDeAmizade(algoz.Assinatura):0.###} (era {laco:0.###})");
			// E NEM PRA QUEM ASSISTE: sem esta metade o problema so mudaria de lugar -- quem treina
			// perderia os amigos DOS amigos, e ninguem ligaria uma coisa a outra.
			Checa("...**e nem a plateia se afasta de quem treina com o amigo dela**",
				  vitima.Social.PontosDeAmizade(algoz.Assinatura) == plateia,
				  $"{vitima.Social.PontosDeAmizade(algoz.Assinatura):0.###} (era {plateia:0.###})");

			// (2) A MORTE NAO PERGUNTA NADA -- amigo que mata e o unico que consegue trair, e e o caso
			// que o dono nomeou primeiro (*"ou TE MATAM"*).
			AoPerderALuta(amigo, algoz, morreu: true);

			Checa("**morrer nas maos dele custa 60 e derruba pro NEGATIVO**",
				  amigo.Social.PontosDeAmizade(algoz.Assinatura)
					  == laco - Convivio.PerdaPorMorte,
				  $"{amigo.Social.PontosDeAmizade(algoz.Assinatura):0.###}");
			Checa("...e por isso ele agora e INIMIGO", amigo.Social.EhInimigo(algoz.Assinatura),
				  Convivio.RotuloDeProximidade(amigo.Social.PontosDeAmizade(algoz.Assinatura)));
			Checa("...**e a mesma conta foi cobrada no livro dele** (amizade e relacao, nao campo de um so)",
				  algoz.Social.PontosDeAmizade(amigo.Assinatura) == laco - Convivio.PerdaPorMorte,
				  $"{algoz.Social.PontosDeAmizade(amigo.Assinatura):0.###}");

			// (3) E ISTO E O QUE "SER INIMIGO" FAZ, sem uma linha de sistema novo:
			Checa("ser inimigo DECLARA a rivalidade sozinho (ninguem clicou em nada)",
				  amigo.Social.EhRival(algoz.Assinatura), "");
			Checa("...e por isso o odio, que so corre contra rival declarado, comecou a correr",
				  amigo.Social.PontosDeInimizade(algoz.Assinatura) == Convivio.PerdaPorMorte,
				  $"{amigo.Social.PontosDeInimizade(algoz.Assinatura):0}");
			Checa("...e o golpe dele passa a alimentar esse odio (`ENMITY_HIT`)",
				  MedirGolpeDeRival(amigo, algoz) == Convivio.InimizadePorGolpe,
				  $"{MedirGolpeDeRival(amigo, algoz)}");
			Checa("...e **conviver com ele nao perdoa mais nada**",
				  RenderiaAmizade(amigo, algoz) == false, "");
			Checa("...e ele nao volta a enfurecer ninguem por voce: o luto le a amizade, e ela e negativa",
				  !amigo.Social.LutoPorMorte(algoz.Assinatura), "");

			// ============================ E UMA DECLARACAO QUE SOBREVIVE, PRA IR PRO DISCO ============================
			// A da secao 8 era com o `algoz`, e a regra nova acabou de derruba-la -- corretamente. Como
			// a secao 9 confere que RELACAO DECLARADA persiste, ela precisa de uma declaracao viva, e
			// ela tem que ser de alguem que continue amigo. **Isto e o avesso do bloco acima**: la o
			// afeto cai com o afeto, aqui ele fica de pe com quem nunca te fez nada.
			if (amigo.Social.Ficha(vitima.Assinatura) is { } fv) fv.Familiaridade = 100;
			VerboRelacao(amigo, $"{vitima.Assinatura}|muito bom");
			Checa("a declaracao de afeto de quem CONTINUA amigo fica de pe",
				  amigo.Social.RelacaoCom(vitima.Assinatura) == Relacao.MuitoBom,
				  amigo.Social.RelacaoCom(vitima.Assinatura).ToString());

			// ============================ 9. ISSO TUDO SOBREVIVE AO LOGOUT ============================
			ASobrevivenciaNoDisco(amigo, vitima, algoz, Checa);

			// ============================ 10. E CADA FAMILIA COM O DEFEITO INJETADO ============================
			AsMutacoesDoConvivio(amigo, algoz, cobaiaA, cobaiaB, Checa);
		}
		finally
		{
			foreach (ServerPlayer p in new[] { amigo, vitima, algoz, longe, lenda, cobaiaA, cobaiaB })
			{
				_players.Remove(p.Id);
				ZoneList(zona.Hash).Remove(p);
			}
		}
	}

	/// <summary>
	/// ============================ AS SEIS FAMILIAS DO CONVIVIO, CADA UMA COM O DEFEITO INJETADO ============================
	/// As secoes 1-9 afirmam. Esta pergunta se elas SABEM FICAR VERMELHAS -- e ela usa o
	/// <see cref="Mutacao"/> da bancada da prova, que e a receita da casa: **mede, estraga, mede,
	/// conserta, mede**. Tres linhas por familia, e a do meio e a unica que interessa.
	///
	/// POR QUE ISTO PRECISAVA EXISTIR AQUI: o convivio inteiro e feito de numeros que sobem e descem
	/// sozinhos, e a maneira classica de uma bancada assim passar verde por vacuo e medir um efeito
	/// que aconteceria de qualquer jeito -- "a amizade nao mudou" fica verde num sistema em que a
	/// amizade nunca muda, e "ele e inimigo" fica verde num sistema que declara todo mundo inimigo.
	/// Cada mutacao abaixo e um defeito que este projeto poderia ter de verdade, e cinco delas sao
	/// defeitos que ele JA TEVE ou quase teve (estao nomeados nos comentarios do
	/// `GameServer.Convivio.cs` e do `Core/Social/Convivio.cs`).
	///
	/// OS CORPOS DA MUTACAO SAO OUTROS (`cobaiaA`/`cobaiaB`, a 100 tiles): o criterio de tres das
	/// familias precisa MATAR pra medir, e ele roda tres vezes -- fazer isso com o par que as secoes
	/// 1-9 mediram apagaria o estado que elas construiram.
	/// ==================================================================================================================
	/// </summary>
	private void AsMutacoesDoConvivio(ServerPlayer amigo, ServerPlayer algoz,
									  ServerPlayer cobaiaA, ServerPlayer cobaiaB,
									  Action<string, bool, string> Checa)
	{
		Checagem Mutar = (n, c, d) => Checa(n, c, d);

		void Zerar(ServerPlayer p, ServerPlayer q)
		{
			p.Social.Amizade.Remove(q.Assinatura);
			p.Social.Rivais.Remove(q.Assinatura);
			p.Social.Inimizade.Remove(q.Assinatura);
			q.Social.Amizade.Remove(p.Assinatura);
			q.Social.Rivais.Remove(p.Assinatura);
			q.Social.Inimizade.Remove(p.Assinatura);
		}

		// =====================================================================
		// (1) CONVIVER FAZ AMIGO -- e o defeito e a DISTANCIA
		// =====================================================================
		// O criterio atravessa o `TickDoConvivio` de verdade, 500 vezes. O defeito injetado nao e um
		// numero trocado: e o par 40 tiles separado, fora do `FRIEND_RANGE` de 6. Sem ele, "500 passos
		// fazem amigo" ficaria verde numa versao que fizesse amigo de quem esta em outro continente --
		// que e o oposto exato do que o dono pediu (*"ficou BASTANTE TEMPO COM VC"*).
		Vec2 ondeEle = cobaiaB.Pos;

		bool VinteCincoMinutosFazemAmigo()
		{
			Zerar(cobaiaA, cobaiaB);
			// SO OS DOIS ANDAM: o prazo (`ProximaAproximacao`) e por pessoa, e a bancada roda dentro
			// de um quadro so -- os outros corpos ficam presos na propria cadencia de 3 s e nao
			// acumulam nada enquanto estes 500 passos correm.
			for (int i = 0; i < 500; i++)
			{
				cobaiaA.ProximaAproximacao = 0;
				cobaiaB.ProximaAproximacao = 0;
				TickDoConvivio();
			}
			return cobaiaA.Social.EhAmigo(cobaiaB.Assinatura)
				&& cobaiaB.Social.EhAmigo(cobaiaA.Assinatura);
		}

		Mutacao(Mutar,
			$"**{Convivio.MinutosParaVirarAmigo:0} MINUTOS lado a lado fazem amigo**, pelo tique de verdade",
			"os dois passam a viver a 40 tiles um do outro (fora do `FRIEND_RANGE` de 6)",
			VinteCincoMinutosFazemAmigo,
			() => cobaiaB.Pos = cobaiaA.Pos + new Vec2(40 * ZoneCollision.TileSize, 0),
			() => cobaiaB.Pos = ondeEle);

		// =====================================================================
		// (2) MATAR CUSTA 60, NOS DOIS LIVROS -- e o defeito e o `EhPessoa`
		// =====================================================================
		// O defeito injetado e o corte que o dono perguntou por escrito ("e o cidadao?"): o algoz vira
		// um corpo do mundo (`Papel` preenchido) e o funil para de cobrar. Ele prova as duas pontas de
		// uma vez -- que a familia mede o FUNIL, e que NPC realmente nao entra nisto. Se ficasse verde
		// com o `Papel` preenchido, amizade com cidadao seria uma fabrica de Super Saiyajin.
		var papelDeMentira = new Jandirus.Core.Npc.PapelDeNpc(
			new Jandirus.Core.Npc.MoldeDeNpc { Id = "bancada", Nome = "cobaia" }, 0);

		bool MorteCobraOsDoisLivros()
		{
			Zerar(cobaiaA, cobaiaB);
			cobaiaA.Social.AceitarAmizade(cobaiaB.Assinatura);
			cobaiaB.Social.AceitarAmizade(cobaiaA.Assinatura);
			double antes = cobaiaA.Social.PontosDeAmizade(cobaiaB.Assinatura);

			AoPerderALuta(cobaiaA, cobaiaB, morreu: true);

			return cobaiaA.Social.PontosDeAmizade(cobaiaB.Assinatura) == antes - Convivio.PerdaPorMorte
				&& cobaiaB.Social.PontosDeAmizade(cobaiaA.Assinatura) == antes - Convivio.PerdaPorMorte;
		}

		Mutacao(Mutar,
			$"**matar custa {Convivio.PerdaPorMorte:0} nos DOIS livros**, pelo funil `AoPerderALuta`",
			"o algoz vira um NPC do mundo (`Papel` preenchido) -- o corte do `EhPessoa`",
			MorteCobraOsDoisLivros,
			() => cobaiaB.Papel = papelDeMentira,
			() => cobaiaB.Papel = null);

		// =====================================================================
		// (3) O SPAR NAO CUSTA NADA -- e o defeito e deixar de ser amigo
		// =====================================================================
		// "O nocaute nao cobrou nada" e a checagem mais facil do mundo de deixar verde por engano:
		// basta o nocaute nao cobrar nunca. O defeito injetado tira a amizade ANTES da queda, e ai o
		// `AlgozEhInimigo` (`Death.dm:75`) responde "isto nao era treino" -- e o mesmo nocaute passa a
		// custar 25. E a prova de que a trava do spar e uma PERGUNTA, e nao um `return` mudo.
		bool eramAmigosAntesDaQueda = true;

		bool OSparNaoCustaNada()
		{
			Zerar(cobaiaA, cobaiaB);
			if (eramAmigosAntesDaQueda)
			{
				cobaiaA.Social.AceitarAmizade(cobaiaB.Assinatura);
				cobaiaB.Social.AceitarAmizade(cobaiaA.Assinatura);
			}
			double antes = cobaiaA.Social.PontosDeAmizade(cobaiaB.Assinatura);

			AoPerderALuta(cobaiaA, cobaiaB, morreu: false);

			return cobaiaA.Social.PontosDeAmizade(cobaiaB.Assinatura) == antes;
		}

		Mutacao(Mutar,
			"**o nocaute entre AMIGOS (o spar) nao custa um ponto** -- treinar com o melhor amigo nao "
			+ "derruba o vinculo",
			"os dois deixam de ser amigos antes da queda: o `AlgozEhInimigo` passa a dizer que aquilo "
			+ "era briga de verdade",
			OSparNaoCustaNada,
			() => eramAmigosAntesDaQueda = false,
			() => eramAmigosAntesDaQueda = true);

		// =====================================================================
		// (4) SER INIMIGO DECLARA A RIVALIDADE -- e o defeito e o `AlternarRival`
		// =====================================================================
		// O defeito e o que o cabecalho do `TornarRival` diz que teria acontecido se o `Afastar`
		// tivesse chamado o verb de alternancia: matar quem voce JA odiava o promoveria de volta a
		// nao-rival, e o odio pararia de crescer exatamente contra quem mais merece.
		Mutacao(Mutar,
			"**cair pro negativo DECLARA a rivalidade sozinho**, e e ela que destrava o odio por golpe",
			"a rivalidade e retirada (o que o `AlternarRival` teria feito no lugar do `TornarRival`)",
			() => amigo.Social.EhInimigo(algoz.Assinatura)
				  && amigo.Social.EhRival(algoz.Assinatura)
				  && MedirGolpeDeRival(amigo, algoz) == Convivio.InimizadePorGolpe,
			() => amigo.Social.Rivais.Remove(algoz.Assinatura),
			() => amigo.Social.TornarRival(algoz.Assinatura));

		// =====================================================================
		// (5) A DECLARACAO DE AFETO CAI JUNTO -- e o defeito e ela sobreviver
		// =====================================================================
		// **ESTE E O BURACO QUE A BANCADA AO VIVO ACHOU**, e ele nao e cosmetico: com a declaracao de
		// pe, a vitima entraria em FURIA pela morte do homem que a matou (`LutoPorMorte`), e --
		// pior -- o `AlgozEhInimigo` continuaria dizendo "nao e inimigo", calando a plateia na proxima
		// vez que esse sujeito a matasse. O criterio le as duas metades.
		if (amigo.Social.Ficha(algoz.Assinatura) is null)
			amigo.Social.Conhecidos[algoz.Assinatura] = new Conhecido
			{
				Assinatura = algoz.Assinatura, Nome = algoz.Name, Familiaridade = 100,
			};
		Conhecido fichaDoAlgoz = amigo.Social.Ficha(algoz.Assinatura)!;
		Relacao relacaoDeVerdade = fichaDoAlgoz.Relacao;

		Mutacao(Mutar,
			"**quem te matou para de te enfurecer E vira inimigo aos olhos da plateia** (a declaracao "
			+ "de afeto caiu junto com o afeto)",
			"a declaracao 'muito bom' pelo proprio assassino sobrevive a morte",
			() => !amigo.Social.LutoPorMorte(algoz.Assinatura)
				  && amigo.Social.AlgozEhInimigo(algoz.Assinatura),
			() => fichaDoAlgoz.Relacao = Relacao.MuitoBom,
			() => fichaDoAlgoz.Relacao = relacaoDeVerdade);

		// =====================================================================
		// (6) O NEGATIVO ATRAVESSA O DISCO -- e o defeito e "higienizar" o numero
		// =====================================================================
		// A secao 9 grava e le pelo `AccountStore` de verdade e diz "o inimigo continua inimigo". Falta
		// saber se ela LE O NUMERO ou se ela sempre diz que sim. O defeito injetado e o conserto que
		// alguem faria de boa fe -- "amizade nao pode ser negativa, vou zerar" --, e o criterio inteiro
		// (ida, volta, e os DOIS lados) tem que cair com ele.
		double negativoDeVerdade = amigo.Social.PontosDeAmizade(algoz.Assinatura);

		bool OInimigoVoltaDoDisco()
		{
			string pasta = Path.Combine(Path.GetTempPath(),
										"jandirus_mutacao_" + Guid.NewGuid().ToString("N"));
			try
			{
				var loja = new AccountStore(pasta);
				var conta = new AccountSave { Conta = "bancada_mutacao" };
				conta.Slots[0] = AccountStore.DeJogador(amigo, 0);
				conta.Slots[1] = AccountStore.DeJogador(algoz, 0);
				loja.Gravar(conta);

				AccountSave? volta = loja.Carregar("bancada_mutacao");
				Convivio? meu = volta?.Slots[0]?.Social;
				Convivio? dele = volta?.Slots[1]?.Social;
				return meu != null && dele != null
					&& meu.EhInimigo(algoz.Assinatura) && dele.EhInimigo(amigo.Assinatura);
			}
			catch (Exception e) { GD.Print($"[bancada] mutacao do disco: {e.Message}"); return false; }
			finally
			{
				try { if (Directory.Exists(pasta)) Directory.Delete(pasta, recursive: true); }
				catch (Exception e) { GD.Print($"[bancada] nao consegui apagar {pasta}: {e.Message}"); }
			}
		}

		Mutacao(Mutar,
			"**o negativo atravessa o disco nos dois lados** (o inimigo continua inimigo depois do save)",
			"o numero negativo e 'higienizado' pra zero antes de gravar",
			OInimigoVoltaDoDisco,
			() => amigo.Social.Amizade[algoz.Assinatura] = 0,
			() => amigo.Social.Amizade[algoz.Assinatura] = negativoDeVerdade);

		// =====================================================================
		// (7) A PERDA E MUTUA -- e o defeito e cobrar de um lado so
		// =====================================================================
		// O `Afastar` sozinho e a forma que este codigo teve antes do `AfastarNosDoisLados`, e o
		// estrago dela e MECANICO e nao cosmetico: o `AlgozEhInimigo` le a lista da VITIMA e o luto le
		// a de quem ASSISTE, entao um lado desatualizado muda quem entra em furia na proxima morte.
		bool cobraOsDoisLados = true;

		bool AContaBateNosDoisLivros()
		{
			Zerar(cobaiaA, cobaiaB);
			cobaiaA.Social.AceitarAmizade(cobaiaB.Assinatura);
			cobaiaB.Social.AceitarAmizade(cobaiaA.Assinatura);
			double antes = cobaiaB.Social.PontosDeAmizade(cobaiaA.Assinatura);

			AoPerderALuta(cobaiaA, cobaiaB, morreu: true);
			// O MUTANTE: o livro do algoz volta ao que era -- exatamente o que sobraria se so a vitima
			// fosse cobrada.
			if (!cobraOsDoisLados) cobaiaB.Social.Amizade[cobaiaA.Assinatura] = antes;

			return cobaiaA.Social.PontosDeAmizade(cobaiaB.Assinatura)
				   == cobaiaB.Social.PontosDeAmizade(cobaiaA.Assinatura);
		}

		Mutacao(Mutar,
			"**a perda e MUTUA** -- os dois livros ficam com a mesma conta (amizade e relacao, nao "
			+ "campo de um so)",
			"so o lado de quem sofreu e cobrado (o `Afastar` sozinho, antes do `AfastarNosDoisLados`)",
			AContaBateNosDoisLivros,
			() => cobraOsDoisLados = false,
			() => cobraOsDoisLados = true);

		// O ESTADO DAS COBAIAS NAO IMPORTA DAQUI PRA FRENTE (elas saem do mundo no `finally`), mas o
		// par `amigo`/`algoz` foi mexido por duas mutacoes -- e as duas desfizeram no `finally` do
		// proprio `Mutacao`. Esta linha e a conferencia de que sobrou o mundo que a secao 8 deixou.
		Checa("...e depois de sete defeitos injetados e desfeitos, o par medido nas secoes 1-9 esta "
			  + "como estava", amigo.Social.EhInimigo(algoz.Assinatura)
			  && amigo.Social.PontosDeAmizade(algoz.Assinatura) == negativoDeVerdade
			  && amigo.Social.EhRival(algoz.Assinatura)
			  && fichaDoAlgoz.Relacao == relacaoDeVerdade,
			  $"{amigo.Social.PontosDeAmizade(algoz.Assinatura):0.###}, rival="
			  + $"{amigo.Social.EhRival(algoz.Assinatura)}, relacao={fichaDoAlgoz.Relacao}");
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
	/// QUANTO ODIO UM GOLPE DELE RENDE? So pra bancada -- bate, mede e desfaz, como o
	/// <see cref="RenderiaAmizade"/>. Serve pra provar que o inimigo automatico ligou de verdade no
	/// eixo que ja existia (`ENMITY_HIT`, `CombatMovement.dm:225`) em vez de so mudar um numero.
	/// </summary>
	private static double MedirGolpeDeRival(ServerPlayer vitima, ServerPlayer autor)
	{
		double antes = vitima.Social.PontosDeInimizade(autor.Assinatura);
		GolpeDeRival(vitima, autor);
		double depois = vitima.Social.PontosDeInimizade(autor.Assinatura);
		vitima.Social.Inimizade[autor.Assinatura] = antes;   // desfaz
		return depois - antes;
	}

	/// <summary>
	/// UM PASSO DE PROXIMIDADE RENDERIA AMIZADE ENTRE ESTES DOIS? So pra bancada -- mede sem
	/// deixar rastro, comparando os pontos antes e depois de um passo.
	/// </summary>
	private static bool RenderiaAmizade(ServerPlayer eu, ServerPlayer outro)
	{
		double antes = eu.Social.PontosDeAmizade(outro.Assinatura);
		eu.Social.Aproximar(outro.Assinatura);
		double depois = eu.Social.PontosDeAmizade(outro.Assinatura);
		if (depois > antes) eu.Social.Amizade[outro.Assinatura] = antes;   // desfaz
		return depois > antes;
	}

	/// <summary>
	/// ============================ O CONVIVIO ATRAVESSA O DISCO? ============================
	/// Esta e a checagem que uma bancada de Core NAO pode fazer, e ela ja teria pego um defeito
	/// real deste projeto: as cores de roupa foram escritas, lidas e usadas por meses **sem nunca
	/// persistir**, porque o campo era `readonly` e o `System.Text.Json` o ignorava em silencio.
	/// O mesmo silencio aqui apagaria a lista de amigos de todo mundo a cada logout -- e o sintoma
	/// seria "o SSJ1 as vezes nao vem", que ninguem liga a um serializador.
	///
	/// Ela grava e le pelo `AccountStore` DE VERDADE, com as opcoes de verdade, numa pasta
	/// temporaria que e apagada no fim. Reimplementar as opcoes aqui testaria a bancada.
	/// =======================================================================================
	/// </summary>
	private static void ASobrevivenciaNoDisco(ServerPlayer amigo, ServerPlayer vitima,
											  ServerPlayer inimigo, Action<string, bool, string> Checa)
	{
		string pasta = Path.Combine(Path.GetTempPath(), "jandirus_convivio_" + Guid.NewGuid().ToString("N"));
		try
		{
			var loja = new AccountStore(pasta);
			var conta = new AccountSave { Conta = "bancada_convivio" };

			// **OS DOIS LADOS VAO PRO DISCO**, e nao so o de quem sofreu: uma relacao sao dois numeros,
			// um em cada save, e gravar so um deles deixaria o assassino com o morto ainda listado como
			// amigo na proxima carga -- que e MECANICO e nao cosmetico (o luto e a raiva leem essas
			// listas). Dois slots da mesma conta de bancada bastam: o que se mede e o ida-e-volta do
			// `Convivio`, e ele nao sabe de que conta veio.
			conta.Slots[0] = AccountStore.DeJogador(amigo, 0);
			conta.Slots[1] = AccountStore.DeJogador(inimigo, 0);
			loja.Gravar(conta);

			AccountSave? volta = loja.Carregar("bancada_convivio");
			Convivio? social = volta?.Slots[0]?.Social;
			Convivio? doOutro = volta?.Slots[1]?.Social;

			Checa("o convivio volta do disco (a conta e o slot foram lidos)", social != null, "nulo");
			if (social == null) return;

			Checa("...com os pontos de amizade intactos",
				  Math.Abs(social.PontosDeAmizade(vitima.Assinatura)
						   - amigo.Social.PontosDeAmizade(vitima.Assinatura)) < 1e-6,
				  $"{social.PontosDeAmizade(vitima.Assinatura):0.###}");
			Checa("...e ainda dizendo que aquela pessoa e AMIGA (e o que a raiva le)",
				  social.EhAmigo(vitima.Assinatura), "");
			Checa("...com a ficha de quem foi visto",
				  social.Ficha(vitima.Assinatura)?.Nome == vitima.Name,
				  social.Ficha(vitima.Assinatura)?.Nome ?? "sem ficha");
			Checa("...e com a familiaridade e a relacao declaradas",
				  social.Conhecidos.Values.Any(c => c.Familiaridade > 0)
				  && social.Conhecidos.Values.Any(c => c.Relacao != Relacao.Nenhuma),
				  string.Join(",", social.Conhecidos.Values.Select(c => $"{c.Nome}:{c.Familiaridade}:{c.Relacao}")));

			// ============================ E A INIMIZADE TAMBEM ATRAVESSA ============================
			// O numero NEGATIVO e o campo mais novo deste sistema e o que corre mais risco de sumir
			// calado: ele mora no MESMO `Dictionary<string,double>` que ja persistia, entao nao houve
			// mudanca de formato nenhuma -- e e exatamente por isso que ninguem iria conferir.
			Checa("**o inimigo continua inimigo depois do disco (o negativo persiste)**",
				  social.EhInimigo(inimigo.Assinatura)
				  && social.PontosDeAmizade(inimigo.Assinatura)
					 == amigo.Social.PontosDeAmizade(inimigo.Assinatura),
				  $"{social.PontosDeAmizade(inimigo.Assinatura):0.###}");
			Checa("...com a rivalidade automatica e o odio que ela destravou",
				  social.EhRival(inimigo.Assinatura)
				  && social.PontosDeInimizade(inimigo.Assinatura) > 0,
				  $"rival={social.EhRival(inimigo.Assinatura)}, odio="
				  + $"{social.PontosDeInimizade(inimigo.Assinatura):0}");
			Checa("...**e o OUTRO LADO voltou do disco com a mesma conta** (a relacao tem dois donos)",
				  doOutro != null && doOutro.EhInimigo(amigo.Assinatura)
				  && doOutro.PontosDeAmizade(amigo.Assinatura)
					 == social.PontosDeAmizade(inimigo.Assinatura),
				  $"{doOutro?.PontosDeAmizade(amigo.Assinatura):0.###}");
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
}
