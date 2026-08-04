using Godot;
using Jandirus.Core.Combat;
using Jandirus.Net;
using LiteNetLib;
using LiteNetLib.Utils;

namespace Jandirus.Server;

/// <summary>
/// AS FERIDAS QUE TODO MUNDO VE.
///
/// ============================ POR QUE O SERVIDOR PRECISA MANDAR ISTO ============================
/// O dano por membro ja existia e ja era rastreado -- mas so o DONO do corpo sabia dele
/// (`S2C.Corpo` sai pro dono e mais ninguem, e e certo que seja assim: saber que o braco do outro
/// esta quebrado vale uma luta).
///
/// Desenhar ferida no sprite muda a conta: um corpo ensanguentado se ve de LONGE, e se cada cliente
/// inventasse as proprias feridas, dois jogadores olhando o mesmo lutador veriam corpos diferentes.
/// O dono apontou isso antes de eu escrever uma linha: "o server teria q sincronizar as feridas pra
/// n ficar estranho". Entao a mascara e calculada no servidor, uma vez, e viaja.
/// ================================================================================================
///
/// O QUE VIAJA E A MASCARA, e nao a vida dos membros: cinco bytes que dizem quanto cada REGIAO
/// esta marcada, sem dizer qual membro nem quanto de vida ele tem. O sigilo do estado de combate
/// continua inteiro -- ve-se que o outro esta destrocado, nao QUAO destrocado.
///
/// CADENCIA: junto do tique de ficha (5 Hz) e so quando MUDA, como todo o resto do estado lento.
/// Dano por membro acontece quando alguem apanha, nao trinta vezes por segundo.
/// </summary>
public sealed partial class GameServer
{
	/// <summary>
	/// Recalcula a mascara de cada corpo e manda o que mudou.
	///
	/// Roda no MESMO tique da ficha, mas num laco PROPRIO -- o laco da ficha tem um `continue`
	/// quando nada mudou nela, e a vida de um membro pode mudar sem que a vida media mude o
	/// bastante pra disparar aquele pacote. Pendurar as feridas la deixaria a ferida esperando
	/// uma mudanca de outro campo pra sair.
	/// </summary>
	private void TickDasFeridas()
	{
		foreach (ServerPlayer pl in _players.Values)
		{
			if (pl.Combate?.Corpo is not { } corpo) continue;

			MascaraDeFeridas agora = Feridas.De(corpo);
			if (agora == pl.EnvFeridas) continue;
			pl.EnvFeridas = agora;
			MandarFeridas(pl);
		}
	}

	/// <summary>Ligado por `--feridateste`. Ver <see cref="FerirDeTeste"/>.</summary>
	private bool _feridaDeTeste;

	/// <summary>
	/// BANCADA: uma ESCADA de estrago, uma regiao por degrau.
	///
	/// As duas fases do efeito -- o roxo e depois o sangue -- acontecem separadas por minutos numa
	/// luta de verdade, e uma foto so pega uma delas. Aqui o corpo nasce com cabeca quase inteira,
	/// braco batido, torso feio, abdomen sangrando e perna no limite: as duas fases e o meio do
	/// caminho, na mesma tela.
	///
	/// Os valores sao FRACAO DE VIDA. Lembrando dos pisos que o combate impoe: golpe nao-letal
	/// nunca leva abaixo de 19,8%, e levantar de um nocaute devolve tudo a 30% -- entao 0.15 aqui
	/// e um estado que so a luta LETAL alcanca.
	/// </summary>
	private static void FerirDeTeste(ServerPlayer pl)
	{
		if (pl.Combate?.Corpo is not { } corpo) return;

		// VESTE O BONECO. Sem roupa nao ha o que rasgar -- e o rasgo e METADE do efeito (foi o que
		// o dono pediu pra roupa: "deixaria regioes transparentes pra representar q ela ta rasgada").
		// Um personagem criado pela linha de comando nasce sem nenhuma peca.
		if (pl.Visual.Roupa.Count == 0)
			pl.Visual.Roupa =
			[
				"res://Assets/Sprites/Clothes/Clothes_GiTop.tres",
				"res://Assets/Sprites/Clothes/Clothes_GiBottom.tres",
			];

		double Por(string zona) => zona switch
		{
			"cabeca" => 0.85,    // quase inteiro: nada aparece
			"bracos" => 0.60,    // roxo entrando
			"torso" => 0.40,     // roxo cheio, sangue comecando
			"abdomen" => 0.22,   // sangue de verdade
			_ => 0.15,           // pernas: no limite, so em luta letal
		};

		foreach (BodyPart p in corpo.Partes) p.Vida = p.VidaMax * Por(p.Zona);

		// E ARRANCA UM BRACO E UMA PERNA, de lados opostos: o teste precisa provar que some o
		// membro CERTO, e dois do mesmo lado nao distinguiriam "some o esquerdo" de "some o lado
		// esquerdo da imagem".
		foreach (BodyPart p in corpo.Partes)
			if (p.Nome is "Braco direito" or "Mao direita" or "Perna esquerda" or "Pe esquerdo")
				{ p.Decepado = true; p.Vida = 0; }

		corpo.Vida();
		pl.Combate.SincronizarVida();
	}

	/// <summary>Manda a mascara de um corpo pra todo mundo que esta na zona dele (o dono incluso).</summary>
	private void MandarFeridas(ServerPlayer de)
	{
		NetDataWriter w = PacoteDeFeridas(de);
		foreach (ServerPlayer o in ZoneList(de.Zone.Hash))
			o.Peer?.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}

	private static NetDataWriter PacoteDeFeridas(ServerPlayer de)
	{
		var w = Protocol.Begin(Protocol.S2C.Feridas);
		w.Put(de.Id);
		w.PutFeridas(de.EnvFeridas);
		return w;
	}

	/// <summary>
	/// APRESENTA AS FERIDAS a quem chega numa zona, e as de quem ja estava a ele.
	///
	/// Sem isto o corpo machucado ficaria limpo pra quem chegou depois -- a mesma familia de
	/// defeito que este projeto ja pegou nas construcoes, nas portas e no cenario derrubado: o
	/// pacote existe, sai UMA vez, e quem nao estava presente naquele instante nunca soube.
	/// </summary>
	private void TrocarFeridas(ServerPlayer novo)
	{
		if (novo.Combate?.Corpo is { } corpo) novo.EnvFeridas = Feridas.De(corpo);

		List<ServerPlayer> zona = ZoneList(novo.Zone.Hash);
		NetDataWriter minhas = PacoteDeFeridas(novo);

		foreach (ServerPlayer outro in zona)
		{
			outro.Peer?.Send(minhas, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
			if (outro != novo)
				novo.Peer?.Send(PacoteDeFeridas(outro), Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
		}
	}
}
