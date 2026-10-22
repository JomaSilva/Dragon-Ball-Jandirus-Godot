using Godot;
using Jandirus.Core.Forms;
using Jandirus.Core.World;
using Jandirus.Net;
using LiteNetLib;
using LiteNetLib.Utils;

namespace Jandirus.Server;

/// <summary>
/// O CLIMA -- o que cai do céu, e quem pode mandar nele.
///
/// ============================ DUAS CAMADAS, E SÓ UMA CUSTA REDE ============================
/// 1. O clima NATURAL é função pura de (ficha do planeta, tempo do mundo). Como o tempo já é
///    sincronizado desde o login, cliente e servidor chegam à mesma chuva sozinhos -- este
///    arquivo nem precisa mandar nada pra ela existir.
/// 2. O clima FORÇADO é estado, porque é decisão de alguém: uma transformação, um ritual, um
///    verb de admin. Esse viaja (`S2C.Clima`), e é a razão de existir do resto do arquivo.
/// ============================================================================================
///
/// A LISTA DE CLIMAS DE CADA PLANETA VEM DO DM (`allowedWeatherTypes`, extraído pro
/// `planetas.json`): Vegeta chove SANGUE e não água, Namek tem a chuva dele, Vampa só conhece
/// tempestade de areia. O ciclo é que não é o de lá -- ver o comentário do <see cref="Clima"/>.
/// </summary>
public sealed partial class GameServer
{
	/// <summary>
	/// OS CLIMAS FORÇADOS, por zona. Some sozinho quando o prazo vence.
	///
	/// Por ZONA e não por jogador porque clima é do LUGAR: quem escurece o céu escurece pra todo
	/// mundo que está ali, inclusive pra quem chegar no meio. É essa a diferença entre um efeito
	/// de tela e um acontecimento no mundo.
	/// </summary>
	private readonly Dictionary<ulong, ClimaForcado> _climaForcado = [];

	/// <summary>A ficha de clima de uma zona: o que pode cair aqui.</summary>
	public ClimaDoPlaneta ClimaDaZona(ZoneKey zona) => Clima.DaZona(zona, _planetas);

	/// <summary>
	/// O céu meteorológico de uma zona agora -- natural, ou o que mandaram.
	///
	/// (2026-10-21) O FORCADO DE UMA SUB-ZONA E O DO PLANETA DELA: o temporal que a Terra recebeu cai no Lookout e na caverna dela (a
	/// ficha natural ja vem do planeta pelo Core). Sem a posicao o Lookout inteiro e Terra -- quem tem o corpo pergunta pelo
	/// <see cref="ClimaDe"/>, que acha o castelo do Rei Vegeta. Ver `GameServer.SubZonas.cs`.
	/// </summary>
	public EstadoDoClima ClimaAgora(ZoneKey zona) => Clima.De(
		ClimaDaZona(zona), TempoDoMundo, Clima.SalDaZona(zona),
		_climaForcado.TryGetValue((PlateiaSoDaZonaDeTeste ? zona : SubZonas.ZonaDoCeu(zona)).Hash, out ClimaForcado f) ? f : default);

	/// <summary>O céu meteorológico de onde ESTE corpo está -- o do planeta da sub-zona, e o de Vegeta no castelo do Lookout.</summary>
	public EstadoDoClima ClimaDe(ServerPlayer pl) => ClimaAgora(PlanetaDoCorpo(pl));

	// =====================================================================
	// O GANCHO: MANDAR NO CÉU
	// =====================================================================
	/// <summary>
	/// ============================ O GANCHO DO CLIMA ============================
	/// FORÇA UM CLIMA NUMA ZONA. É a porta única -- toda coisa do jogo que queira mexer no céu
	/// entra por aqui, e não escrevendo no dicionário nem inventando um efeito de tela próprio.
	///
	/// QUEM JÁ USA: as transformações (<see cref="ClimaPorTransformacao"/>).
	/// QUEM VAI USAR, e por isso isto é público e genérico:
	///   * as técnicas que mexem no ambiente;
	///   * o ritual `e_change_weather` do DM (`Rituals_Manipulation.dm:271-279`), que faz
	///     exatamente isto -- escrever `currentWeather` numa área;
	///   * os eventos de chefe e a morte de planeta (lá o clima vira "Destruction");
	///   * um verb de admin.
	///
	/// `segundos <= 0` LIMPA o forçado e devolve o céu ao ciclo natural. Sem esse caminho,
	/// desfazer seria impossível e um erro de duração prenderia a zona numa tempestade.
	/// ===========================================================================
	/// </summary>
	public void ForcarClima(ZoneKey zona, TipoDeClima tipo, double segundos, double forca = 1,
							string motivo = "")
	{
		// (2026-10-21) QUEM FORCA O CEU DE UMA SUB-ZONA FORCA O DO PLANETA DELA: o ceu do Lookout E o da Terra (`SubZonas`), e um
		// temporal guardado na chave do Lookout nao chegaria a ninguem -- quem esta la le o da Terra. Vale pros chamadores que passam
		// `pl.Zone` (o Super Shenron, as fotos, o trailer). Sem a posicao o Lookout inteiro e Terra: o castelo do Rei Vegeta chega aqui
		// ja resolvido por quem tem o corpo (`PlanetaDoCorpo`).
		if (!PlateiaSoDaZonaDeTeste) zona = SubZonas.ZonaDoCeu(zona);

		// ============================ `segundos <= 0` CANCELA; `Limpo` COM PRAZO **FORCA CEU ABERTO** ============================
		// Antes as duas coisas eram a mesma linha (`segundos <= 0 || tipo == Limpo` cancelava), e com
		// isso **nao havia como pedir ceu aberto**: a unica saida era devolver a zona ao ciclo natural
		// e torcer. Isso e um buraco de verdade e nao um capricho de bancada -- o ritual
		// `e_change_weather` do DM escreve `currentWeather` com qualquer valor, inclusive nenhum, e a
		// bancada de FOTO precisa de um palco sem chuva de sangue passando na frente do assunto (as
		// primeiras fotos do cabelo sairam ilegiveis por isso).
		//
		// Os dois caminhos continuam distintos e nenhum chamador mudou: todos os que passam `Limpo`
		// passam `0` junto (conferido: `AdminClimaNatural`, e os tres da morte de planeta).
		// `Clima.De` ja sabia tratar um forcado `Limpo` -- ele devolve `Tipo = Limpo`, que nao desenha
		// nada; era so aqui que ele nunca chegava a ser gravado.
		// ==================================================================================================================
		if (segundos <= 0)
		{
			if (!_climaForcado.Remove(zona.Hash)) return;
			AnunciarClima(zona, default);
			GD.Print($"[clima] {zona.Name}: o ceu volta ao normal");
			return;
		}

		// O MAIS FORTE VENCE, e não o mais recente. Dois SSJ subindo junto não podem fazer o céu
		// piscar entre as duas tempestades; e um SSJ1 chegando depois de um SSJ3 não tem por que
		// aliviar o temporal que o outro abriu.
		double agora = TempoDoMundo;
		if (_climaForcado.TryGetValue(zona.Hash, out ClimaForcado atual) && atual.Vivo(agora)
			&& atual.Forca > forca && atual.Ate >= agora + segundos) return;

		var novo = new ClimaForcado
		{
			Tipo = tipo,
			Ate = agora + segundos,
			Duracao = segundos,
			Forca = Math.Clamp(forca, 0, 1),
			Motivo = motivo,
		};
		_climaForcado[zona.Hash] = novo;
		AnunciarClima(zona, novo);

		GD.Print($"[clima] {zona.Name}: {Clima.Nome(tipo)} por {segundos:0}s "
				 + $"(forca {novo.Forca:0.00}){(motivo.Length > 0 ? " -- " + motivo : "")}");
	}

	/// <summary>
	/// ============================ O MESMO CÉU, MAIS FORTE ============================
	/// Sobe a FORÇA de um clima já forçado sem tocar em tipo, prazo nem duração. É o que uma RAMPA
	/// precisa -- a agonia de um planeta que vai explodir em cinco minutos tem que escurecer o céu
	/// progressivamente, e não escurecê-lo de uma vez no segundo zero.
	///
	/// ============================ POR QUE NÃO DÁ PRA REEMITIR O `ForcarClima` ============================
	/// Porque `Clima.De` deriva a entrada suave de `decorrido = Duracao - (Ate - agora)`, e o
	/// `ForcarClima` reescreve os dois: logo depois de reemitir, `decorrido` volta a ZERO e a
	/// `entradaRapida` de 1,2 s puxa a força a zero junto. O céu do fim do mundo **piscaria pra limpo**
	/// a cada reemissão -- e uma rampa por reemissão são dezenas delas.
	///
	/// Preservando `Ate` e `Duracao`, `decorrido` continua crescendo e a curva de entrada/saída fica
	/// exatamente como estava. O que muda é só o teto que ela multiplica.
	/// ================================================================================================
	///
	/// ============================ E ELA SÓ SOBE ============================
	/// Mesma disciplina do "o MAIS FORTE VENCE" do <see cref="ForcarClima"/>: um pedido mais fraco não
	/// alivia o que já está no céu. Aqui isso também protege a rampa de si mesma -- o tique roda a
	/// 1 Hz e um único segundo com a conta errada não pode desfazer cinco minutos de agonia.
	///
	/// O PASSO DE 0,05 NÃO É MICROTUNING: sem ele isto viraria **um pacote confiável por pessoa da
	/// zona por segundo**, durante 310 s, pra mexer no terceiro decimal de uma força. Com ele são ~11
	/// pacotes na agonia inteira, e cada um deles é um degrau que o jogador de fato enxerga.
	/// ======================================================================
	/// </summary>
	/// <returns>Verdadeiro quando a força de fato subiu (e o pacote saiu).</returns>
	public bool ApertarClima(ZoneKey zona, double forca, string motivo = "")
	{
		if (!_climaForcado.TryGetValue(zona.Hash, out ClimaForcado atual)) return false;
		if (!atual.Vivo(TempoDoMundo)) return false;

		double alvo = Math.Clamp(forca, 0, 1);
		if (alvo <= atual.Forca + 0.049) return false;

		// `with` E NAO ESCRITA DIRETA: `ClimaForcado` e `readonly struct` de proposito (ele viaja no
		// fio e e copiado pelo palco de bancada), entao a copia com um campo trocado e o unico jeito
		// -- e ela mantem `Ate` e `Duracao` intactos, que e o ponto inteiro deste metodo.
		atual = atual with { Forca = alvo };
		_climaForcado[zona.Hash] = atual;
		AnunciarClima(zona, atual);

		GD.Print($"[clima] {zona.Name}: o ceu aperta pra {alvo:0.00}"
				 + (motivo.Length > 0 ? " -- " + motivo : ""));
		return true;
	}

	/// <summary>
	/// ============================ O GANCHO DAS TRANSFORMAÇÕES ============================
	/// UMA FORMA MUDOU, E O CÉU PODE RESPONDER. Chamado do funil único de troca de forma
	/// (`GameServer.Formas.AnunciarForma`), então já vale pra subir, pra descer e pra queda por
	/// Ki acabado -- sem precisar lembrar de chamar em três lugares.
	///
	/// A TABELA ESTÁ VAZIA DE PROPÓSITO. O encanamento está pronto e provado; QUAIS formas mexem
	/// no céu, com que clima e por quanto tempo, é decisão de balanceamento que ainda não foi
	/// tomada. Ligar isso antes da hora daria ao jogo um comportamento que ninguém escolheu.
	///
	/// PRA LIGAR UMA FORMA basta uma linha em <see cref="CeuDaForma"/>; nada mais precisa saber
	/// que ela existe. Enquanto isso, dá pra ver qualquer clima na hora pelo painel de admin
	/// (aba Admin -> "Clima deste planeta"), que chama o mesmo <see cref="ForcarClima"/>.
	/// =====================================================================================
	/// </summary>
	private void ClimaPorTransformacao(ServerPlayer pl, string de, string para)
	{
		if (CeuDaForma(para) is not { } efeito) return;

		// SÓ SUBINDO. Descer da forma não abre tempestade -- e sem esta guarda o `AnunciarForma`
		// da queda por Ki zerado chamaria o céu do degrau de CHEGADA, que é a base.
		//
		// "Subir" agora é a ORDEM DENTRO DA LINHA (era a comparação de dois inteiros). Trocar de
		// linha -- do SSJ3 pro Blue, por exemplo -- conta como subir, porque a linha divina não
		// tem como ser um degrau abaixo de nada.
		FormaDef? dDe = Catalogo.Def(de), dPara = Catalogo.Def(para);
		if (dPara == null) return;
		if (dDe != null && dDe.Linha == dPara.Linha && dPara.Ordem <= dDe.Ordem) return;

		ForcarClima(pl.Zone, efeito.Tipo, efeito.Segundos, efeito.Forca,
					$"{pl.Name} em {dPara.Nome}");

		if (efeito.Fala.Length > 0)
			foreach (ServerPlayer o in ZoneList(pl.Zone.Hash)) Avisar(o, efeito.Fala);
	}

	/// <summary>O que uma forma faz com o céu. Nulo = não faz nada.</summary>
	private readonly record struct CeuDeForma(TipoDeClima Tipo, double Segundos, double Forca, string Fala);

	/// <summary>
	/// QUAIS FORMAS MEXEM NO CÉU -- hoje, nenhuma.
	///
	/// O molde de uma entrada, pra quando for a hora (o SSJ2 do anime é a forma dos raios; o SSJ3
	/// é a que escurece o mundo):
	///
	/// <code>
	/// "ssj2" => new(TipoDeClima.Tempestade, 45, 0.75, "o ar fica pesado e um estalo corre pelo ceu."),
	/// </code>
	///
	/// UM CUIDADO PRA QUANDO LIGAR: o SSJ1 é a forma que se usa por horas. Se toda transformação
	/// fechar o tempo, fechar o tempo deixa de significar alguma coisa -- o céu tem que ser a
	/// assinatura do que é raro.
	/// </summary>
	private static CeuDeForma? CeuDaForma(string id) => id switch
	{
		_ => null,
	};

	// =====================================================================
	// ADMIN
	// =====================================================================
	/// <summary>
	/// Quanto dura um clima forçado pelo painel de admin, em segundos.
	///
	/// Vinte minutos: tempo de sobra pra olhar o efeito de todos os ângulos sem precisar clicar de
	/// novo, e curto o bastante pra um servidor não ficar preso numa nevasca porque alguém esqueceu.
	/// O botão "voltar ao natural" é a saída imediata.
	/// </summary>
	private const double SegundosDoClimaDeAdmin = 20 * 60;

	/// <summary>
	/// O `Force Weather` DO PAINEL DE ADMIN: escolhe um clima e ele cai nesta zona agora.
	///
	/// ============================ ISTO NÃO É PORT, É FERRAMENTA ============================
	/// O original tem um `mob/Admin3/verb/ForceWeather()` (`Modules/Turfs/MakyoCreation.dm:20`),
	/// mas o nome engana: a lista de "weather type" dele tem duas opções, "Cancel" e "Makyo Star",
	/// e o que ele faz é ligar `IsHellstar` numa área. Não existia, lá, um jeito de olhar a chuva.
	///
	/// Aqui existe porque precisa existir: o ciclo natural corre em blocos de seis minutos e
	/// sorteia entre os tipos DO PLANETA, então ver a nevasca de Icer pode levar meia hora de
	/// espera -- e um efeito que só dá pra conferir em meia hora não é conferido.
	/// =======================================================================================
	///
	/// O ARGUMENTO É O NOME DO TIPO (`Tempestade`, `ChuvaDeSangue`, ...) ou o nome do DM
	/// ("Blood Rain"), com uma força opcional depois da barra: `Tempestade|0.5`.
	/// </summary>
	private void AdminClima(ServerPlayer adm, string arg)
	{
		string[] partes = arg.Split('|', StringSplitOptions.TrimEntries);
		string nome = partes.Length > 0 ? partes[0] : "";

		if (nome.Length == 0)
		{
			Avisar(adm, "escolha o clima. Aqui podem cair: " + ClimasDaMinhaZona(adm));
			return;
		}

		if (!Enum.TryParse(nome, ignoreCase: true, out TipoDeClima tipo))
			tipo = Clima.DoNomeDoDm(nome);

		if (tipo == TipoDeClima.Limpo)
		{
			Avisar(adm, $"nao conheco o clima '{nome}'. Os que existem: "
						+ string.Join(", ", Enum.GetNames<TipoDeClima>()));
			return;
		}

		double forca = 1;
		if (partes.Length > 1 && double.TryParse(partes[1], System.Globalization.NumberStyles.Float,
												 System.Globalization.CultureInfo.InvariantCulture, out double f))
			forca = Math.Clamp(f, 0.05, 1);

		// FORÇA O CLIMA MESMO QUE O PLANETA NÃO O TENHA NA LISTA, e avisa. É ferramenta de
		// depuração: recusar "neve em Vampa" tiraria justamente o caso que se quer olhar (o
		// desenho da neve) por causa de uma regra que vale pro SORTEIO, não pro comando.
		//
		// (2026-10-21) O CEU DE ONDE O ADMIN ESTA: no Lookout e na caverna o da Terra, no castelo do Rei Vegeta o de Vegeta -- e e o
		// nome DELE que o aviso diz, pra quem forcou saber que mexeu no planeta inteiro. Ver `GameServer.SubZonas.cs`.
		ZoneKey ceu = PlanetaDoCorpo(adm);
		ClimaDoPlaneta daqui = ClimaDaZona(ceu);
		bool estranho = Array.IndexOf(daqui.Permitidos, tipo) < 0;

		ForcarClima(ceu, tipo, SegundosDoClimaDeAdmin, forca, $"admin {adm.Name}");

		Avisar(adm, $"{Clima.Nome(tipo)} em {ceu.Name} por {SegundosDoClimaDeAdmin / 60:0} min"
					+ (forca < 1 ? $", a {forca:P0} de forca" : "") + "."
					+ (estranho ? $" (este clima NAO cai aqui naturalmente -- aqui podem cair: {ClimasDaMinhaZona(adm)})" : ""));
		Registrar(adm, $"forcou {Clima.Nome(tipo)} em {ceu.Name}");
	}

	/// <summary>Devolve o céu desta zona ao ciclo natural.</summary>
	private void AdminClimaNatural(ServerPlayer adm)
	{
		ZoneKey ceu = PlanetaDoCorpo(adm);
		if (!_climaForcado.ContainsKey(ceu.Hash))
		{
			Avisar(adm, "o ceu daqui ja esta no ciclo natural.");
			return;
		}
		ForcarClima(ceu, TipoDeClima.Limpo, 0);
		Avisar(adm, $"o ceu de {ceu.Name} volta ao ciclo natural.");
		Registrar(adm, $"soltou o clima de {ceu.Name}");
	}

	/// <summary>A ficha de clima da zona -- o que cai aqui, e o que esta caindo agora.</summary>
	private void AdminClimaFicha(ServerPlayer adm)
	{
		ZoneKey ceu = PlanetaDoCorpo(adm);
		ClimaDoPlaneta daqui = ClimaDaZona(ceu);
		EstadoDoClima agora = ClimaAgora(ceu);

		Avisar(adm, $"--- ceu de {ceu.Name} ---");
		Avisar(adm, $"agora: {Clima.Nome(agora.Tipo)} a {agora.Forca:P0}"
					+ (agora.Forcado ? " (FORCADO)" : " (natural)")
					+ $" | cinza {agora.Cinza:P0} | encobre {agora.Encobre:P0}");
		Avisar(adm, "pode cair aqui: " + ClimasDaMinhaZona(adm));
		if (!daqui.Existe) Avisar(adm, "este lugar nao tem clima nenhum (o `HasWeather=0` do DM).");
	}

	private string ClimasDaMinhaZona(ServerPlayer adm)
	{
		ClimaDoPlaneta c = ClimaDaZona(PlanetaDoCorpo(adm));
		return c.Existe && c.Permitidos.Length > 0
			? string.Join(", ", c.Permitidos.Select(Clima.Nome))
			: "nada";
	}

	// =====================================================================
	// O FIO
	// =====================================================================
	private static NetDataWriter PacoteDeClima(ClimaForcado f)
	{
		var w = Protocol.Begin(Protocol.S2C.Clima);
		w.Put((byte)f.Tipo);
		w.Put(f.Ate);
		w.Put(f.Duracao);
		w.Put((float)f.Forca);
		return w;
	}

	/// <summary>
	/// O CEU FORCADO MUDOU: vai pra quem esta debaixo dele. (2026-10-21) Debaixo do ceu da Terra esta tambem quem esta no Lookout e na
	/// caverna dela -- a plateia e a do CEU (`PlateiaDoCeu`), e nao so a da zona. Ver `GameServer.SubZonas.cs`.
	/// </summary>
	private void AnunciarClima(ZoneKey zona, ClimaForcado f)
	{
		NetDataWriter w = PacoteDeClima(f);
		foreach (ServerPlayer o in PlateiaDoCeu(zona))
		{
			EscutaDoClimaMandado?.Add((o.Id, zona.Hash, f.Tipo));
			o.Peer?.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
		}
	}

	/// <summary>
	/// Manda o clima forçado da zona pra UM jogador. Chamado no login e na troca de zona.
	///
	/// SEM ISTO, quem chega no meio de um temporal não vê temporal nenhum: o pacote saiu quando
	/// ele não estava, e o clima forçado não se deriva do tempo pra ele se corrigir sozinho. É a
	/// mesma família de defeito que já mordeu as portas, as construções e as feridas.
	///
	/// (2026-10-21) O DO CEU DE ONDE ELE ESTA: no Lookout e na caverna, o do planeta; no castelo do Rei Vegeta, o de Vegeta. E quem cruza
	/// a fronteira do castelo dentro do Lookout recebe o do outro ceu no tique seguinte (`TickDasSubZonas`), que le o que ficou anotado.
	/// </summary>
	private void MandarClima(ServerPlayer pl)
	{
		ZoneKey ceu = PlanetaDoCorpo(pl);
		_climaForcado.TryGetValue(ceu.Hash, out ClimaForcado f);
		if (!f.Vivo(TempoDoMundo)) f = default;
		_ceuDoClimaMandado[pl.Id] = ceu.Hash;
		EscutaDoClimaMandado?.Add((pl.Id, ceu.Hash, f.Tipo));
		pl.Peer?.Send(PacoteDeClima(f), Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}

	// =====================================================================
	// O RAIO
	// =====================================================================
	/// <summary>Espera entre raios numa zona, em segundos. Sorteada a cada estouro.</summary>
	private const double EsperaDeRaioMin = 3.0, EsperaDeRaioMax = 11.0;

	/// <summary>
	/// A que distância do jogador um raio pode cair, em pixels.
	///
	/// O MÍNIMO É O QUE IMPORTA: um raio que cai em cima de você toda vez vira um efeito de tela
	/// disfarçado. Longe o bastante pra às vezes estar fora do quadro é o que faz existirem os
	/// dois casos -- ver o risco, e só ouvir o trovão.
	/// </summary>
	private const float RaioPertoMin = 6 * 32, RaioPertoMax = 46 * 32;

	/// <summary>Quando a próxima descarga cai em cada zona (tempo do mundo, em segundos).</summary>
	private readonly Dictionary<ulong, double> _proximoRaio = [];

	private readonly Random _sorteioDoRaio = new();

	/// <summary>
	/// ============================ O RAIO CAI NO MUNDO, E TODO MUNDO CONCORDA ============================
	/// Ele era sorteado dentro de cada cliente. Cada jogador via os próprios raios, e dois amigos
	/// lado a lado numa tempestade não viam a mesma tempestade -- o que é a diferença entre um
	/// clima e um protetor de tela rodando em paralelo.
	///
	/// Agora o servidor escolhe ONDE, e conta pra zona. Quem está com a câmera naquele pedaço vê o
	/// risco; quem não está vê o clarão e ouve o trovão atrasado pela distância (ver
	/// `ClimaNaTela`). É o que dá tamanho à tempestade: dá pra saber que caiu longe, e para que lado.
	///
	/// A ESCOLHA É PERTO DE ALGUÉM, e não num ponto qualquer do planeta. Um mapa tem 500x500
	/// tiles; sorteando uniformemente, quase todo raio cairia onde não há ninguém e a tempestade
	/// seria um chat dizendo que trovejou. O DM faz o mesmo -- ele sorteia um átomo do `oview()`
	/// do jogador (`Weather.dm:245-254`).
	/// =====================================================================================================
	/// </summary>
	private void TickDoRaio(double agora)
	{
		foreach ((ulong hash, List<ServerPlayer> zona) in _zones)
		{
			if (zona.Count == 0) continue;

			// (2026-10-21) UMA ZONA, MAIS DE UM CEU: o Lookout tem o da Terra e, no castelo do Rei Vegeta, o de Vegeta (`SubZonas`). Cada
			// ceu tem o proprio relogio de descarga e so puxa raio quem esta debaixo dele -- senao o temporal da Terra cairia no patio do
			// castelo, debaixo do ceu limpo de Vegeta. Toda outra zona tem um ceu so, e segue com a lista dela e a chave de sempre.
			if (!SubZonas.TemDoisCeus(zona[0].Zone))
			{
				RaioNoCeu(hash, SubZonas.ZonaDoCeu(zona[0].Zone), zona, zona, agora);
				continue;
			}
			foreach (ZoneKey ceu in SubZonas.CeusDaZona(zona[0].Zone))
			{
				List<ServerPlayer> debaixo = zona.FindAll(p => PlanetaDoCorpo(p).Equals(ceu));
				if (debaixo.Count > 0) RaioNoCeu(hash ^ ceu.Hash, ceu, debaixo, zona, agora);
				else _proximoRaio.Remove(hash ^ ceu.Hash);
			}
		}
	}

	/// <summary>
	/// UMA DESCARGA (ou nao) DESTE CEU, perto de alguem de <paramref name="debaixo"/>, contada pra <paramref name="zona"/> inteira -- o
	/// ponto e da zona, e quem esta nela ve o risco ou o clarao. <paramref name="chave"/> e o relogio de descarga deste ceu nesta zona.
	/// </summary>
	private void RaioNoCeu(ulong chave, ZoneKey ceu, List<ServerPlayer> debaixo, List<ServerPlayer> zona, double agora)
	{
		EstadoDoClima clima = ClimaAgora(ceu);
		if (!Clima.TemRaio(clima.Tipo) || clima.Forca < 0.45)
		{
			_proximoRaio.Remove(chave);
			return;
		}

		if (!_proximoRaio.TryGetValue(chave, out double quando))
		{
			_proximoRaio[chave] = agora + Sortear(EsperaDeRaioMin, EsperaDeRaioMax);
			return;
		}
		if (agora < quando) return;

		// MAIS RAIO NUMA TEMPESTADE MAIS FORTE -- é como ela se anuncia sem precisar de texto.
		double aperto = 1.9 - clima.Forca;
		_proximoRaio[chave] = agora + Sortear(EsperaDeRaioMin, EsperaDeRaioMax) * aperto;

		// perto de ALGUÉM debaixo deste ceu, e não do mesmo alguém sempre
		if (SortearQuemPuxaORaio(debaixo, clima.Tipo) is not { } perto) return;
		double ang = _sorteioDoRaio.NextDouble() * Math.PI * 2;
		double dist = Sortear(RaioPertoMin, RaioPertoMax);
		var onde = new Vec2(
			perto.Pos.X + (float)(Math.Cos(ang) * dist),
			perto.Pos.Y + (float)(Math.Sin(ang) * dist));

		EscutaDosRaios?.Add((perto.Id, ceu.Hash));
		var w = Protocol.Begin(Protocol.S2C.Raio);
		w.PutVec(onde);
		w.Put((float)(_sorteioDoRaio.NextDouble() * 1000));
		foreach (ServerPlayer o in zona)
			o.Peer?.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}

	/// <summary>
	/// PERTO DE QUEM O RAIO CAI. Nulo = ninguem desta zona puxa raio agora, e a descarga nao acontece.
	///
	/// ============================ QUEM ESTA SOB TETO NAO PUXA RAIO ============================
	/// No DM a descarga da tempestade sai do `doWeatherEffects()` de cada mob
	/// (`Weather.dm:243-254`), que le o clima da AREA DO MOB -- e a area interna nao tem clima
	/// nenhum (`Weather.dm:76-80`). Dentro de casa ninguem sorteia raio; se a zona inteira esta
	/// abrigada, nao cai raio nela.
	///
	/// ONDE ele cai continua livre, como la: o DM poe a descarga num atomo qualquer do `oview()` de
	/// quem a puxou, e o `oview()` de quem esta na rua alcanca o telhado do vizinho.
	///
	/// A DESTRUICAO DO PLANETA E A EXCECAO (ver `CelulaInterna.TiraOClima`): o laco dela roda pra
	/// toda area do planeta, a interna inclusive (`Area_Death.dm:81-98`).
	///
	/// DUAS PASSADAS E NENHUMA LISTA: conta quem esta ao ar livre, sorteia um indice, anda ate ele.
	/// Isto roda uma vez por descarga (de 3 em 3 s no minimo), entao o que importa e nao alocar.
	/// ==========================================================================================
	/// </summary>
	private ServerPlayer? SortearQuemPuxaORaio(List<ServerPlayer> zona, TipoDeClima tipo)
	{
		if (zona.Count == 0) return null;
		if (!CelulaInterna.TiraOClima(tipo)) return zona[_sorteioDoRaio.Next(zona.Count)];

		int aoArLivre = 0;
		foreach (ServerPlayer p in zona)
			if (!SobTeto(p)) aoArLivre++;
		if (aoArLivre == 0) return null;

		int k = _sorteioDoRaio.Next(aoArLivre);
		foreach (ServerPlayer p in zona)
			if (!SobTeto(p) && k-- == 0) return p;
		return null;
	}

	private double Sortear(double a, double b) => a + _sorteioDoRaio.NextDouble() * (b - a);

	/// <summary>
	/// Varre os forçados vencidos. Roda junto do tique do céu (1 Hz).
	///
	/// O CLIENTE NÃO PRECISA DO AVISO DE FIM -- ele tem o prazo e sabe quando expira sozinho.
	/// Isto aqui é só faxina de memória: sem ela o dicionário guardaria uma entrada por zona que
	/// já teve tempestade, pra sempre.
	/// </summary>
	private void TickDoClima()
	{
		double agora = TempoDoMundo;
		if (_climaForcado.Count == 0) return;

		List<ulong>? mortos = null;
		foreach ((ulong zona, ClimaForcado f) in _climaForcado)
			if (!f.Vivo(agora)) (mortos ??= []).Add(zona);

		if (mortos == null) return;
		foreach (ulong z in mortos) _climaForcado.Remove(z);
	}

	// =====================================================================
	// A BANCADA
	// =====================================================================
	/// <summary>
	/// `--climateste <tipo>`: força um clima em quem entrar, sem prazo prático.
	///
	/// Existe porque o clima natural corre em blocos de seis minutos e sorteia entre os tipos do
	/// planeta -- esperar uma nevasca em Icer pra conferir o desenho dela pode levar meia hora, e
	/// um efeito que só dá pra ver em meia hora não é conferido.
	/// </summary>
	private TipoDeClima _climaDeTeste = TipoDeClima.Limpo;

	private void LerBancadaDoClima(string[] args)
	{
		int i = Array.IndexOf(args, "--climateste");
		if (i < 0 || i + 1 >= args.Length) return;

		string nome = args[i + 1];
		if (!Enum.TryParse(nome, ignoreCase: true, out TipoDeClima t))
			t = Clima.DoNomeDoDm(nome);

		if (t == TipoDeClima.Limpo)
		{
			GD.PushWarning($"[server] BANCADA: nao conheco o clima '{nome}' -- use "
						   + string.Join(", ", Enum.GetNames<TipoDeClima>()));
			return;
		}

		_climaDeTeste = t;
		GD.Print($"[server] BANCADA: clima travado em {Clima.Nome(t)}");
	}

	/// <summary>
	/// Aplica o clima da bancada no ceu de quem entrou. (2026-10-21) O CEU, e nao a zona: quem entra no castelo do Rei Vegeta trava o de
	/// Vegeta, e no Lookout o da Terra (que e o mesmo de la). Ver `GameServer.SubZonas.cs`.
	/// </summary>
	private void ClimaDeTeste(ServerPlayer pl)
	{
		if (_climaDeTeste == TipoDeClima.Limpo) return;
		ForcarClima(PlanetaDoCorpo(pl), _climaDeTeste, 3600, 1, "bancada");
	}
}
