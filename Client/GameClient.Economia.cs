using Jandirus.Core.Economia;
using Jandirus.Core.Tech;
using LiteNetLib.Utils;

namespace Jandirus.Client;

/// <summary>
/// A ECONOMIA NO CLIENTE -- a carteira (zeni + recursos) e a mineracao, como o servidor as contou por ultimo.
/// O que se faz com isto mora nas telas: a aba Tech (`MenuJogo.Tech.cs`), a Stats e a faixa da mineracao
/// (`PainelDeMineracao`). Pedido do dono de 2026-10-10; a regra esta em `Core/Economia/Recursos.cs`.
/// </summary>
public partial class GameClient
{
	/// <summary>
	/// RECURSOS NO BOLSO: a materia-prima que se tira dos planetas e que paga obra ao dobro do preco em zeni.
	/// Chega pela carteira (`S2C.Carteira`), junto do <see cref="Zeni"/>.
	/// </summary>
	public double Recursos { get; private set; }

	/// <summary>
	/// A MINHA MINERACAO, como o servidor a descreveu no ultimo pacote: se esta de pe, com que ferramenta, quanto
	/// o ultimo segundo rendeu e como esta a reserva do planeta (0 a 1). Ver `Protocol.S2C.Mineracao`.
	/// </summary>
	public readonly record struct MineracaoVista(bool Ativa, Ferramenta Ferramenta, float PorSegundo, float Reserva);

	public MineracaoVista Mineracao { get; private set; }

	/// <summary>
	/// O SERVIDOR CONTOU COMO ESTA A MINHA MINERACAO: de pe (`true`, uma vez por segundo enquanto durar) ou parada
	/// (`false`: secou, foi interrompida, ou o pedido foi negado). Quem escuta e o corpo
	/// (`LocalPlayer.OnMineracao`), que e o dono da atividade.
	/// </summary>
	public event Action<bool>? MineracaoMudou;

	/// <summary>
	/// A VISTA DA MINERACAO NAO SOBREVIVE A MINERACAO: quem para por conta propria nao recebe pacote (ja sabe), e
	/// o que fica guardado e da leva anterior -- outra ferramenta, talvez outro planeta. A faixa nao pode abrir
	/// mostrando aquilo; quem chama e o corpo, ao comecar outra (`LocalPlayer.LerAtividade`).
	/// </summary>
	public void EsquecerMineracao() => Mineracao = default;

	/// <summary>
	/// O VEREDITO DE UMA OFERTA DO CATALOGO NESTA MOEDA, com a carteira de AGORA.
	///
	/// O `Recusa` que veio no catalogo e o do servidor no instante do envio, em zeni. Raca e tecnologia ficam
	/// como ele julgou; so a pergunta da carteira e refeita, pela MESMA funcao do Core que o servidor usa ao
	/// cobrar (ver <see cref="CatalogoDeObras.ComSaldo"/>, que conta por que ela e refeita aqui).
	/// </summary>
	public RecusaObra VereditoDe(OfertaDeObra o, Moeda moeda) =>
		CatalogoDeObras.ComSaldo((RecusaObra)o.Recusa, o.Custo, moeda == Moeda.Recursos ? Recursos : Zeni, moeda);

	/// <summary>
	/// A CARTEIRA MUDOU (`S2C.Carteira`): o zeni ou os recursos sao outros. Quem escuta e o menu (as abas Stats e
	/// Tech se reescrevem); a faixa da mineracao e o modo de construir conferem a carteira por quadro.
	///
	/// ============================ EVENTO PROPRIO, E NAO O `TechMudou` ============================
	/// A primeira versao tocava o <see cref="TechMudou"/> ("e o evento que a aba Tech e a Stats ja escutam"). So
	/// que ele e tambem o SINO DO FANTASMA de assentar maquina: a `TelaDeConstrucao` manda o clique e le o
	/// proximo toque como *"o servidor respondeu"* -- se o item ainda esta na mochila, foi recusa, e a previa
	/// volta a seguir o mouse. A carteira chega a cada segundo de mineracao (e a cada parede paga, e a cada zeni
	/// que a vigia do servidor percebe): tocar aquele sino daqui era responder por um pacote que nao e a
	/// resposta, e soltar a previa antes da hora de quem assenta uma maquina enquanto minera.
	/// ==========================================================================================
	/// </summary>
	public event Action? CarteiraMudou;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--diagminerar`): a carteira toca o sino da tecnologia, como na primeira versao.
	/// Lido pela propria leitura do pacote, em <see cref="LerCarteira"/>.
	/// </summary>
	public static bool CarteiraTocaOSinoDaTechDeTeste;

	/// <summary>`S2C.Carteira`: guarda os dois numeros e avisa pelo <see cref="CarteiraMudou"/>.</summary>
	private void LerCarteira(NetDataReader reader)
	{
		Zeni = reader.GetDouble();
		Recursos = reader.GetDouble();
		CarteiraMudou?.Invoke();
		if (CarteiraTocaOSinoDaTechDeTeste) TechMudou?.Invoke();
	}

	/// <summary>
	/// `S2C.Mineracao`. Guarda (quem desenha, a faixa, le o estado por quadro, sem assinatura pra vazar) e avisa
	/// o corpo, que e quem larga a pose quando o servidor para.
	/// </summary>
	private void LerMineracao(NetDataReader reader)
	{
		bool ativa = reader.GetBool();
		var ferramenta = (Ferramenta)reader.GetByte();
		float porSegundo = reader.GetFloat();
		float reserva = reader.GetFloat();
		Mineracao = new MineracaoVista(ativa, ferramenta, porSegundo, reserva);
		MineracaoMudou?.Invoke(ativa);
	}
}
