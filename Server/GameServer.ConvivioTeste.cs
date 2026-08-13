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

		try
		{
			// ============================ 1. CONVIVER NAO FAZ AMIGO ============================
			// O coracao do sistema, e o unico numero que nao pode ser "arredondado" por ninguem: a
			// proximidade para em 49 e a amizade comeca em 50. Sao 600 passos -- seis vezes mais do
			// que os 490 que levariam do zero ao teto --, entao se houvesse vazamento ele apareceria.
			//
			// O PRAZO E PUXADO PRA TRAS a cada volta em vez de esperar 3 s de bancada. E o mesmo
			// gesto que a bancada da raiva usa com as janelas de furia.
			for (int i = 0; i < 600; i++)
			{
				amigo.ProximaAproximacao = 0;
				vitima.ProximaAproximacao = 0;
				longe.ProximaAproximacao = 0;
				algoz.ProximaAproximacao = 0;
				TickDoConvivio();
			}

			double pts = amigo.Social.PontosDeAmizade(vitima.Assinatura);
			Checa("conviver 600 passos poe a amizade no TETO DE CONHECIDO (49), e nem um a mais",
				  Math.Abs(pts - Convivio.TetoDeConhecido) < 1e-6, $"{pts:0.###}");
			Checa("...e conhecido NAO E AMIGO (e o 49 < 50 que faz da amizade um gesto)",
				  !amigo.Social.EhAmigo(vitima.Assinatura), Convivio.RotuloDeProximidade(pts));
			Checa("...e a convivencia foi mutua (o outro lado tambem acumulou)",
				  vitima.Social.EhAmigo(amigo.Assinatura) == false
				  && vitima.Social.PontosDeAmizade(amigo.Assinatura) > 0,
				  $"{vitima.Social.PontosDeAmizade(amigo.Assinatura):0.###}");
			Checa("...e quem estava a 40 tiles nao conheceu ninguem",
				  longe.Social.Conhecidos.Count == 0, $"{longe.Social.Conhecidos.Count}");
			Checa("...e a ficha do conhecido guarda a raca de quando ele foi visto",
				  amigo.Social.Ficha(vitima.Assinatura)?.Raca == "Saiyan",
				  amigo.Social.Ficha(vitima.Assinatura)?.Raca ?? "sem ficha");

			// ============================ 2. O PEDIDO ATRAVESSA O TETO ============================
			// PELOS VERBOS DE VERDADE, e nao escrevendo no dicionario: o caminho do jogador passa
			// pelo alcance (3 tiles), pelo pedido pendente e pela resposta -- e e nele que uma
			// amizade de mao unica nasceria.
			VerboPedirAmizade(vitima, amigo.Id.ToString());
			Checa("o pedido de amizade fica pendurado em quem recebeu",
				  amigo.PedidoDeAmizade == vitima.Assinatura, amigo.PedidoDeAmizade);

			VerboResponderAmizade(amigo, aceitou: true);
			Checa("aceito, ele faz AMIGO dos DOIS LADOS (amizade de mao unica nao existe)",
				  amigo.Social.EhAmigo(vitima.Assinatura) && vitima.Social.EhAmigo(amigo.Assinatura),
				  $"{amigo.Social.PontosDeAmizade(vitima.Assinatura):0} / "
				  + $"{vitima.Social.PontosDeAmizade(amigo.Assinatura):0}");
			Checa("...e o pedido pendente foi consumido", amigo.PedidoDeAmizade.Length == 0, "");

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

			// AGORA O MESMO CORPO VE UM AMIGO MORRER. Quem mata e o `longe`, que a vitima nao conhece
			// (passo 1: `Conhecidos.Count == 0`) -- e portanto e inimigo pelo `Death.dm:75`.
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

			// ============================ 9. ISSO TUDO SOBREVIVE AO LOGOUT ============================
			ASobrevivenciaNoDisco(amigo, vitima, Checa);
		}
		finally
		{
			foreach (ServerPlayer p in new[] { amigo, vitima, algoz, longe, lenda })
			{
				_players.Remove(p.Id);
				ZoneList(zona.Hash).Remove(p);
			}
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
											  Action<string, bool, string> Checa)
	{
		string pasta = Path.Combine(Path.GetTempPath(), "jandirus_convivio_" + Guid.NewGuid().ToString("N"));
		try
		{
			var loja = new AccountStore(pasta);
			var conta = new AccountSave { Conta = "bancada_convivio" };
			conta.Slots[0] = AccountStore.DeJogador(amigo, 0);
			loja.Gravar(conta);

			AccountSave? volta = loja.Carregar("bancada_convivio");
			Convivio? social = volta?.Slots[0]?.Social;

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
