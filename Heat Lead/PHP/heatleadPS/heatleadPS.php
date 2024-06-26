<?php

if (!defined('_PS_VERSION_')) {
    exit;
}

class HeatLeadPS extends Module
{
    private $apiKey;

    public function __construct()
    {
        $this->name = 'heatleadPS';
        $this->tab = 'front_office_features';
        $this->version = '1.3.0';
        $this->author = 'HeatLead - Mateusz Werner';
        $this->need_instance = 0;
        $this->apiKey = 'jefo33RPPjdeojpq0582ojajfwp4j29I3MDAQfje3i';  

        parent::__construct();

        $this->displayName = $this->l('HeatLead Module');
        $this->description = $this->l('Module to add tracking script for HeatLead.');
    }

    public function install()
    {
        return parent::install() && $this->registerHook('displayHeader');
    }

    public function hookDisplayHeader($params)
    {
        if (isset($_GET['heatlead'])) {
            $heatleadValue = $_GET['heatlead'];
            $apiResponse = $this->registerClickAndGetHLTT($heatleadValue);
            if ($apiResponse) {
                $ttlDays = $apiResponse->ttl;
                $hltt = $apiResponse->hltt;
                $expires = time() + (86400 * $ttlDays);
                $this->setHttpOnlyCookie('HLTT', $hltt, $expires);
            }
        }

        if (isset($_GET['id_order']) && isset($_GET['key'])) {
            $this->handleDataSubmission($_GET['id_order'], $_GET['key']);
        }
    }

    private function registerClickAndGetHLTT($heatleadValue)
    {
        $postData = json_encode(array('heatlead' => $heatleadValue));

        $ch = curl_init('https://eksperci.myjki.com/api/link-loaded');
        curl_setopt($ch, CURLOPT_RETURNTRANSFER, true);
        curl_setopt($ch, CURLOPT_HTTPHEADER, array(
            'Content-Type: application/json',
            'Authorization: Bearer ' . $this->apiKey
        ));
        curl_setopt($ch, CURLOPT_POST, true);
        curl_setopt($ch, CURLOPT_POSTFIELDS, $postData);

    
        curl_setopt($ch, CURLOPT_SSL_VERIFYPEER, false);
        curl_setopt($ch, CURLOPT_SSL_VERIFYHOST, false);

        $response = curl_exec($ch);
        curl_close($ch);

        return json_decode($response);
    }

    private function setHttpOnlyCookie($name, $value, $expires)
    {
        setcookie($name, $value, $expires, "/", "", false, true);
    }

    private function handleDataSubmission($orderId, $orderKey)
    {
        if (isset($_COOKIE['HLTT'])) {
            $hltt = $_COOKIE['HLTT'];

            $orderData = $this->getOrderData($orderId);
            if ($orderData && $orderData['secure_key'] == $orderKey) {
                $data = array(
                    'OrderId' => $orderId,
                    'OrderKey' => $orderKey,
                    'HLTT' => $hltt
                );

                $this->postDataToAPI($data);
            }
        }
    }

    private function getOrderData($orderId)
    {
        $sql = new DbQuery();
        $sql->select('id_order, secure_key');
        $sql->from('orders');
        $sql->where('id_order = ' . (int)$orderId);

        return Db::getInstance()->getRow($sql);
    }

    private function postDataToAPI($data)
    {
        $postData = json_encode($data);

        $ch = curl_init('https://eksperci.myjki.com/api/InterceptOrder');
        curl_setopt($ch, CURLOPT_RETURNTRANSFER, true);
        curl_setopt($ch, CURLOPT_HTTPHEADER, array(
            'Content-Type: application/json; charset=UTF-8',
            'Authorization: Bearer ' . $this->apiKey
        ));
        curl_setopt($ch, CURLOPT_POST, true);
        curl_setopt($ch, CURLOPT_POSTFIELDS, $postData);

   
        curl_setopt($ch, CURLOPT_SSL_VERIFYPEER, false);
        curl_setopt($ch, CURLOPT_SSL_VERIFYHOST, false);

        curl_exec($ch);
        curl_close($ch);
    }
}
?>
